using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class GroupPage(DbValues<Participant> participants, DbValues<ApplicationGroup> groups,
                              EventLimits limits, EventDetails details,
                              EmailSender emailSender, TimeProvider timeProvider) : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant)
    {
        if (participant.Status >= ParticipantStatus.Accepted)
        {
            return ForbiddenView();
        }

        if (participant.Status == ParticipantStatus.Finalized)
        {
            if (limits.ApplicationGroupSize <= 1)
            {
                return ForbiddenView();
            }

            var group = await groups.FirstAsync(g => g.Members.Contains(participant));
            return SummaryOnlyView("Application group",
                group.Members.Count == 1
                     ? [("Applying alone", "")]
                     : [("Applying with", string.Join(", ", group.Members.Where(m => m != participant).Select(m => m.FullName)))]
            );
        }

        if (limits.ApplicationGroupSize <= 1)
        {
            return RequiredView("Finalize your application");
        }
        return RequiredView("Application group");
    }

    public override async Task<object?> GetModelAsync(Participant participant)
        => new Model(
               await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant)),
               await GetInvitingGroupsAsync(participant)
           );

    public async Task<StatusMessage> CreateInvitationAsync(Participant participant, string emailAddress)
    {
        if (emailAddress.Equals(participant.EmailAddress, StringComparison.OrdinalIgnoreCase))
        {
            return Error("You cannot invite yourself...");
        }

        var invitee = await participants.FindAsync(emailAddress);
        if (invitee is not null && invitee.Status is >= ParticipantStatus.Finalized or < ParticipantStatus.Created)
        {
            return Error($"{invitee.FullName} has already gone through the application process and cannot be invited.");
        }

        var group = await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant));
        if (group is null)
        {
            var id = DeterministicId.Create(participant.EmailAddress, timeProvider);
            group = new(id) { Members = { participant } };
            groups.Add(group);
        }
        if (group.Members.Count + group.InvitedParticipants.Count == limits.ApplicationGroupSize)
        {
            return Error($"You cannot invite any more members, as the maximum group size is {limits.ApplicationGroupSize}.");
        }

        if (invitee is null)
        {
            invitee = new(emailAddress);
            participants.Add(invitee);
        }

        group.InvitedParticipants.Add(invitee);

        // Always send this even if the invite already existed, the person may have deleted the previous email
        await emailSender.SendEmailAsync(
            recipient: invitee.EmailAddress,
            subject: "Group invitation",
            body: $"{participant.FullName} would like to apply with you to {details}.",
            operation: Operation.CreatePageAction<Participant?, EmailPage>(nameof(EmailPage.ConfirmEmailAddressAsync))
        );

        return Success($"You invited **{invitee.FullName ?? invitee.EmailAddress}**, who must now accept or reject this invitation to join your group.");
    }

    public async Task<StatusMessage> CancelInvitationAsync(Participant participant, string emailAddress)
    {
        var group = await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant));
        if (group is null)
        {
            return Error("You are no longer in a group and thus cannot cancel an invitation.");
        }

        var invitee = group.InvitedParticipants.FirstOrDefault(p => p.EmailAddress.Equals(emailAddress, StringComparison.OrdinalIgnoreCase));
        if (invitee is not null)
        {
            group.InvitedParticipants.Remove(invitee);
            await emailSender.SendEmailAsync(
                recipient: emailAddress,
                subject: "Canceled invitation",
                body: $"{participant.FullName} canceled their invitation to apply with you to {details}.",
                operation: null
            );
        }
        // No point in returning different messages based on whether the invite existed, what would the user do with this knowledge?
        // While this would look weird if the email address had never belonged to their group, that's only possible
        // if the participant manually crafted a request, in which case it's their problem,
        // or if the email address belongs to a participant who changed it in the meantime,
        // which yields not-great UX but should happen very rarely since it requires the change to happen
        // after the user has seen this page but before they've decided to cancel the invite...
        return Success($"You canceled the invitation for **{invitee?.FullName ?? emailAddress}**.");
    }

    public async Task<StatusMessage> AcceptInvitationAsync(Participant participant, string id)
    {
        var group = await groups.FindAsync(id);
        if (group is null)
        {
            return Error("The group whose invitation you wanted to accept no longer exists.");
        }

        if (group.InvitedParticipants.Remove(participant))
        {
            var existing = await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant));
            if (existing is not null)
            {
                existing.Members.Remove(participant);
                if (existing.Members.Count == 0)
                {
                    groups.Remove(existing);
                }
            }

            group.Members.Add(participant);

            var memberNames = string.Join(", ", group.Members.Where(m => m != participant).Select(m => m.FullName));
            return Success($"You joined the group of **{memberNames}**.");
        }

        return Error("The group you requested to join canceled their invitation.");
    }

    public async Task<StatusMessage> RejectInvitationAsync(Participant participant, string id)
    {
        var group = await groups.FindAsync(id);
        if (group is null)
        {
            return Success("The group whose invitation you wanted to accept no longer exists.");
        }

        group.InvitedParticipants.Remove(participant);

        var memberNames = string.Join(", ", group.Members.Select(m => m.FullName));
        return Success($"You rejected the invitation to join the group of **{memberNames}**.");
    }

    public async Task<StatusMessage> RemoveMemberAsync(Participant participant, string emailAddress)
    {
        var group = await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant));
        if (group is null)
        {
            return Error("You are no longer in a group and thus cannot remove a member.");
        }

        var member = group.Members.FirstOrDefault(m => m.EmailAddress.Equals(emailAddress, StringComparison.OrdinalIgnoreCase));
        if (member is null)
        {
            return Error($"It does not look like **{emailAddress}** belongs to your group. Did they change their email address or leave your group already?");
        }

        group.Members.Remove(member);
        // not sure why anyone would do that when alone in a group, but let's ensure we don't have orphaned groups anyway
        if (group.Members.Count == 0)
        {
            groups.Remove(group);
        }

        if (member.EmailAddress.Equals(participant.EmailAddress, StringComparison.OrdinalIgnoreCase))
        {
            return Success("You left your previous group.");
        }

        await emailSender.SendEmailAsync(
            recipient: member.EmailAddress,
            subject: "Application group removal",
            body: $"{participant.FullName} removed you from their application group.",
            operation: null
        );

        return Success($"You removed **{member.FullName}** from your group.");
    }

    public async Task<StatusMessage> FinalizeAsync(Participant participant)
    {
        // This isn't strictly required but 99% of the case if someone does this it means they accidentally forgot to join the group.
        var invitingGroups = await GetInvitingGroupsAsync(participant);
        if (invitingGroups.Count > 0)
        {
            return Error("You cannot finalize while there are pending invitations for you to join a group.");
        }

        var group = await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant));
        if (group is null)
        {
            var id = DeterministicId.Create(participant.EmailAddress, timeProvider);
            group = new(id) { Members = { participant } };
            groups.Add(group);
        }
        if (group.InvitedParticipants.Count > 0)
        {
            return Error("You cannot finalize while your group has pending invitations.");
        }

        // Explicitly stating this avoids participants forgetting to apply with their friends and not realizing until they've been accepted.
        string GetTeamCompositionInfo(Participant member)
        {
            var others = group.Members.Where(m => m != member).Select(m => m.FullName).ToArray();
            return others switch
            {
                [var lone] => $"with {lone}",
                [.. var firsts, var last] => $"with {string.Join(", ", firsts)}, and {last}",
                _ => "on your own",
            };
        }

        var emails = new List<Email>();
        foreach (var member in group.Members)
        {
            member.Status = ParticipantStatus.Finalized;
            emails.Add(new(
                Recipient: member.EmailAddress,
                Subject: "Application finalized",
                Body: $"Your application to {details} is now finalized.\n\n"
                    + (limits.ApplicationGroupSize > 1 ? $"You are applying {GetTeamCompositionInfo(member)}.\n\n" : "")
                    + "You can un-finalize it from the website if you need to make changes.\n\n"
                    + "**This is not an acceptance email**. Please wait for the organizers to make a decision. "
                    + "You will receive an explicit acceptance or rejection email when the decision is made.",
                Operation: null
            ));
        }

        await emailSender.SendAsync(emails);

        group.FinalizationDate = timeProvider.GetUtcNow();

        return Success("You finalized your application.");
    }

    public async Task<StatusMessage> WithdrawAsync(Participant participant)
    {
        if (participant.Status == ParticipantStatus.WithdrawnBeforeConfirmation)
        {
            return NoChange();
        }

        var group = await groups.FirstOrDefaultAsync(g => g.Members.Contains(participant));
        if (group is not null)
        {
            group.Members.Remove(participant);
            if (group.Members.Count == 0)
            {
                groups.Remove(group);
            }
        }

        await emailSender.SendEmailAsync(
            recipient: participant.EmailAddress,
            subject: "Withdrawal",
            body: "You have withdrawn your application. If you would like to undo this, please use the link below.",
            operation: Operation.CreatePageAction<Participant?, WithdrawnPage>(nameof(WithdrawnPage.UndoAsync)),
            operationDescription: "Undo withdrawal"
        );

        participant.Status = ParticipantStatus.WithdrawnBeforeConfirmation;
        return Success("You have withdrawn.");
    }

    private Task<IReadOnlyCollection<ApplicationGroup>> GetInvitingGroupsAsync(Participant participant)
        => groups.Where(g => g.InvitedParticipants.Contains(participant)).ToCollectionAsync();

    public sealed record Model(ApplicationGroup? Group, IReadOnlyCollection<ApplicationGroup> InvitedGroups);
}