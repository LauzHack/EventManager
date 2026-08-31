using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

// Unlike most other actions in this system, "check in" and "cancel check in"
// are not idempotent because they most likely indicate user error,
// e.g., one admin checked someone in and another admin accidentally clicks on the button to check that person in
// when they should've clicked on another check-in button.

public sealed class CheckInPage(DbValues<Participant> participants, DbValues<ApplicationGroup> groups,
                                ConfigValue<EventStatus> eventStatus,
                                EmailSender emailSender, TimeProvider timeProvider) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (eventStatus.Value >= EventStatus.CheckInClosed)
        {
            return EditableView("Late check-in", "Manage");
        }
        return RequiredView("Check-in");
    }

    public override async Task<object?> GetModelAsync(Admin admin)
        => await participants.Where(p => p.Status >= ParticipantStatus.Confirmed)
                             .OrderByName()
                             .ToCollectionAsync();

    public async Task<StatusMessage> CheckInAsync(string emailAddress)
    {
        var participant = await participants.FindAsync(emailAddress);
        if (participant is null)
        {
            return Error($"**{emailAddress}** is not a participant.");
        }
        if (participant.Status == ParticipantStatus.CheckedIn)
        {
            return Error($"**{participant.FullName}** is already checked in.");
        }
        if (participant.Status != ParticipantStatus.Confirmed)
        {
            return Error($"**{participant.FullName}** cannot be checked in this way.");
        }

        participant.Status = ParticipantStatus.CheckedIn;

        if (participant.AdminRemarks is null)
        {
            return Success($"Checked in **{participant.FullName}**.");
        }
        return Success($"Checked in **{participant.FullName}**.\n\n**{participant.AdminRemarks}**");
    }

    public async Task<StatusMessage> CancelCheckInAsync(string emailAddress)
    {
        var participant = await participants.FindAsync(emailAddress);
        if (participant is null)
        {
            return Error($"**{emailAddress}** is not a participant.");
        }
        if (participant.Status != ParticipantStatus.CheckedIn)
        {
            return Error($"**{participant.FullName}** is not checked in.");
        }

        participant.Status = ParticipantStatus.Confirmed;

        return Success($"Canceled check-in for **{participant.FullName}**.");
    }

    public async Task<StatusMessage> CheckInUnknownAsync(string emailAddress, string givenName, string? familyName)
    {
        var participant = await participants.FindAsync(emailAddress);

        string message;
        if (participant is null)
        {
            participant = new Participant(emailAddress)
            {
                Status = ParticipantStatus.CheckedIn,
                GivenName = givenName,
                FamilyName = familyName
            };
            participants.Add(participant);
            // While application groups are useless at this stage, we must maintain the invariant that all participants who finalized have one
            var id = DeterministicId.Create(participant.EmailAddress, timeProvider);
            groups.Add(new(id) { Members = { participant } });
            message = $"Created and checked in **{participant.FullName}** with email address **{participant.EmailAddress}**.";
        }
        else if (participant.Status < ParticipantStatus.CheckedIn)
        {
            participant.Status = ParticipantStatus.CheckedIn;
            participant.GivenName = givenName;
            participant.FamilyName = familyName;
            message = $"Checked in **{participant.FullName}**. (This participant was already in the database)";
        }
        else
        {
            return Error($"{participant.FullName} is already in the database and checked in.");
        }

        await emailSender.SendEmailAsync(
            recipient: participant.EmailAddress,
            subject: "Check-in",
            body: "You have been checked in to the event. Please use the link below to log in.",
            operation: Operation.CreatePageView<Participant>(),
            operationDescription: "Log in"
        );

        return Success(message);
    }

    public async Task<StatusMessage> FinishCheckInAsync(Admin admin)
    {
        if (!admin.IsOwner)
        {
            return Error("Only owners can finish regular check-in");
        }

        eventStatus.Set(EventStatus.CheckInClosed);
        return Success("Regular check-in has ended, though you can still manage it if you want.");
    }

    public async Task<StatusMessage> RestartCheckInAsync(Admin admin)
    {
        if (!admin.IsOwner)
        {
            return Error("Only owners can restart regular check-in");
        }

        eventStatus.Set(EventStatus.CheckInStarted);
        return Success("Regular check-in has started again.");
    }
}