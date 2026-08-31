using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

namespace EventManager.AdminPages;

public sealed class AcceptancePage(DbValues<Participant> participants, DbValues<ApplicationGroup> groups,
                                   ConfigValue<EventStatus> eventStatus, EventDetails eventDetails,
                                   EmailSender emailSender, TimeProvider timeProvider) : Page<Admin>
{
    public sealed record Model(IReadOnlyCollection<ApplicationGroup> FinalizedGroups, IReadOnlyCollection<Participant> AcceptedParticipants);

    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (eventStatus.Value >= EventStatus.CheckInStarted)
        {
            return ForbiddenView();
        }
        if (eventStatus.Value is EventStatus.ApplicationsOpen)
        {
            return RequiredView("Acceptance");
        }
        return EditableView("Acceptance", "Accept/Reject");
    }

    public override async Task<object?> GetModelAsync(Admin admin)
    {
        var finalizedGroups = await groups.Where(g => g.Members.Any(m => m.Status == ParticipantStatus.Finalized)).ToCollectionAsync();
        var acceptedParticipants = await participants
            .Where(p => p.Status >= ParticipantStatus.Accepted)
            .OrderByName()
            .ToCollectionAsync();

        return new Model(finalizedGroups, acceptedParticipants);
    }

    public async Task<StatusMessage> AcceptSpecificAsync(string emailAddress, string givenName, string? familyName)
    {
        var participant = await participants.FindAsync(emailAddress);
        ApplicationGroup? group = null;
        if (participant is null)
        {
            participant = new(emailAddress);
            participants.Add(participant);
        }
        else
        {
            group = await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant));
        }

        if (group is null)
        {
            // While application groups are useless at this stage, we must maintain the invariant that all participants who finalized have one
            var id = DeterministicId.Create(participant.EmailAddress, timeProvider);
            group = new(id) { Members = { participant } };
            groups.Add(group);
        }

        if (participant.Status >= ParticipantStatus.Accepted)
        {
            return Error($"**{participant.FullName}** is already accepted");
        }

        // While the first check is technically redundant, we want specific error messages for user-friendliness
        if (participant.IsSoftRejected)
        {
            return Error($"**{participant.FullName}** has been soft-rejected and cannot be accepted until that changes.");
        }
        var softRejected = group.Members.Where(m => m.IsSoftRejected)
                                        .Select(m => m.FullName)
                                        .ToArray();
        if (softRejected.Length > 0)
        {
            return Error($"**{participant.FullName}** cannot be accepted due to the soft rejection of group members **{string.Join(", ", softRejected)}**.");
        }

        participant.GivenName ??= givenName;
        participant.FamilyName ??= familyName;

        return await AcceptAsync(group);
    }

    public async Task<StatusMessage> RejectSpecificAsync(string emailAddress)
    {
        var participant = await participants.FindAsync(emailAddress);
        if (participant is null)
        {
            return Error($"No participant with email **{emailAddress}**.");
        }

        var group = await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant));
        if (group is null || participant.Status < ParticipantStatus.Finalized)
        {
            return Error($"The participant with email **{emailAddress}** did not finalize their application and can thus not be rejected yet.");
        }

        if (group.Members.Count > 1)
        {
            group.Members.Remove(participant);
            var id = DeterministicId.Create(participant.EmailAddress, timeProvider);
            var newGroup = new ApplicationGroup(id) { Members = { participant } };
            groups.Add(newGroup);
        }

        await RejectAsync([participant]);

        return Success($"Rejected and notified **{participant.EmailAddress}**.");
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Not relevant here, this is not security-related")]
    public async Task<StatusMessage> AcceptAsync(uint count, bool random, string? attribute, bool? equality, string? value)
    {
        var finalized = await groups.Where(g => g.Members.All(p => !p.IsSoftRejected && p.Status == ParticipantStatus.Finalized)).ToCollectionAsync();

        ApplicationGroup[] filtered;
        if (attribute is null)
        {
            filtered = [.. finalized];
        }
        else
        {
            if (value is null || equality is null)
            {
                return Error("If the attribute is set, the value must also be set.");
            }

            if (!equality.Value && finalized.Count > 0 && finalized.All(g => g.Members.All(p => !p.Profile[attribute].Equals(value, StringComparison.OrdinalIgnoreCase))))
            {
                return Error($"You asked for **{attribute}** to not be **'{value}'**, but this is the case for everyone, did you make a typo?");
            }

            // Note the `Any` here, not `All`!
            filtered = [.. finalized.Where(g => g.Members.Any(p => equality == p.Profile[attribute].Equals(value, StringComparison.OrdinalIgnoreCase)))];
        }

        if (random)
        {
            var randomnessSource = new Random((int)timeProvider.GetTimestamp());
            randomnessSource.Shuffle(filtered);
        }
        else
        {
            // note that all status change dates within a group are the same at this point
            Array.Sort(filtered, (a, b) => a.FinalizationDate.CompareTo(b.FinalizationDate));
        }

        int acceptedCount = 0;
        var toAccept = new List<ApplicationGroup>();
        foreach (var group in filtered)
        {
            if (acceptedCount >= count)
            {
                break;
            }
            toAccept.Add(group);
            acceptedCount += group.Members.Count;
        }

        return await AcceptAsync(toAccept);
    }

    public async Task<StatusMessage> CloseAsync(Admin admin)
    {
        if (!admin.IsOwner)
        {
            return Error("Only owners can close applications.");
        }

        if (eventStatus.Value is not EventStatus.ApplicationsOpen)
        {
            return Error("Applications are not open and thus cannot be closed.");
        }

        eventStatus.Set(EventStatus.ApplicationsClosed);
        var toReject = await participants.Where(p => p.Status >= ParticipantStatus.Created && p.Status <= ParticipantStatus.Finalized).ToCollectionAsync();
        await RejectAsync(toReject);
        return Success($"Applications are now closed. All {toReject.Count} non-accepted applicants rejected and notified.");
    }

    private async Task<StatusMessage> AcceptAsync(params IReadOnlyCollection<ApplicationGroup> groups)
    {
        var emails = new List<Email>();
        foreach (var group in groups)
        {
            foreach (var participant in group.Members)
            {
                // We may be accepting someone whose group includes already-accepted members,
                // e.g., if they forgot to confirm but the rest of their group didn't,
                // so explicitly do not re-accept folks who are already accepted.
                if (participant.Status >= ParticipantStatus.Accepted)
                {
                    continue;
                }

                participant.Status = ParticipantStatus.Accepted;
                emails.Add(new Email(
                     Recipient: participant.EmailAddress,
                     Subject: "Acceptance",
                     Body: $"We're happy to inform you you have been accepted to {eventDetails}!\n\n"
                         + "**You must now confirm your participation using the link below**.\n\n"
                         + (group.Members.Count > 1 ? "Each of your group members must confirm using the email they received.\n\n" : "")
                         + "If you can no longer make it, you can withdraw from the event after clicking the link below.\n\n"
                         + "Applications are open until the event organizers explicitly close them. "
                         + "If you have friends who have not been accepted yet, they should wait for an explicit acceptance or rejection email.",
                     Operation: Operation.CreatePageAction<Participant, WaitForAcceptancePage>(nameof(WaitForAcceptancePage.ConfirmAsync)),
                     OperationDescription: "Confirm"
                ));
            }

            group.AcceptanceDate = timeProvider.GetUtcNow();
        }

        await emailSender.SendAsync(emails);

        return Success("Accepted and notified:\n" + string.Join('\n', emails.Select(e => $"- {e.Recipient}")));
    }

    private Task RejectAsync(IReadOnlyCollection<Participant> toReject)
    {
        foreach (var participant in toReject)
        {
            participant.Status = ParticipantStatus.Rejected;
        }

        return emailSender.SendCopyAsync(
            subject: "Rejection",
            body: $"Unfortunately, due to space constraints, we could not accept all applications, and you have been rejected from {eventDetails}. "
                + "We hope you will apply again next time. \n\n"
                + "Please do not write to us asking if you can participate anyway. We have already accepted more people than we can host because we know some people will cancel.\n\n"
                + "_(If you had started an application with this email address, then switched to a different email address for your final application which was accepted, "
                + "as long as you have received an email confirming your participation, you're fine)_",
            recipients: [.. toReject.Select(p => p.EmailAddress)]
        );
    }
}