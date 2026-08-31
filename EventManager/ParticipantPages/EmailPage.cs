using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class EmailPage(DbValues<Participant> participants, DbValues<ApplicationGroup> groups, DbValues<Project> projects, DbValues<TravelExpense> expenses,
                              EmailSender emailSender) : Page<Participant?>
{
    public override async Task<PageView> ViewAsync(Participant? participant) => participant?.Status switch
    {
        null or ParticipantStatus.Created => RequiredView("Application"),
        _ => EditableView("Email", "Edit", ("Address", participant.EmailAddress))
    };

    public async Task<StatusMessage> EditAsync(Participant? participant, string emailAddress, string? referrer)
    {
        if (participant is null)
        {
            participant = await participants.FindAsync(emailAddress);
            if (participant is null)
            {
                participant = new(emailAddress);
                participants.Add(participant);
            }

            var op = Operation.CreatePageAction<Participant?, EmailPage>(nameof(ConfirmEmailAddressAsync));
            if (referrer is not null)
            {
                op = op.WithExtraTextArgument(nameof(referrer), referrer);
            }
            await emailSender.SendEmailAsync(
                recipient: participant.EmailAddress,
                subject: "Log in",
                body: "Welcome! Use the link below to log in and apply.\n\n"
                    + "**We will not process your application until you finalize it**.\n\n"
                    + "_Please do not delete this email, as you will need the link below if your session expires._",
                operation: op,
                operationDescription: "Log in"
            );

            return ImportantInformation(
                $"**Please log in using the link that was just sent to {emailAddress} to continue the application process**.\n\n"
                + "_If you do not get an email, please check your spam folder and try with, e.g., GMail instead of university or company email addresses._"
            );
        }

        if (participant.EmailAddress.Equals(emailAddress, StringComparison.OrdinalIgnoreCase))
        {
            return NoChange();
        }

        var emailOwner = await participants.FindAsync(emailAddress);
        if (emailOwner is null)
        {
            participant.FutureEmailAddress = emailAddress;
            var newParticipant = new Participant(emailAddress);
            participants.Add(newParticipant);
            // The link will log in the target email address and immediately begin the migration process
            await emailSender.SendEmailAsync(
                recipient: newParticipant.EmailAddress,
                subject: "Email address change",
                body: $"Please confirm your email address change from {participant.EmailAddress} to {emailAddress}.\n\n"
                    + "_You may use this link again to log in with this new email address._",
                operation: Operation.CreatePageAction<Participant?, EmailPage>(nameof(ChangeEmailAddressAsync), ("oldEmailAddress", participant.EmailAddress)),
                operationDescription: "Confirm"
            );
            return ImportantInformation($"Please confirm your email address change through the link sent to **{emailAddress}**.");
        }

        return Error($"The email address **{emailAddress}** is already in use.");
    }

    public async Task<StatusMessage> ConfirmEmailAddressAsync(Participant participant, string? referrer)
    {
        if (participant.Status is ParticipantStatus.Created)
        {
            participant.Status = ParticipantStatus.EmailAddressVerified;
            participant.Referrer = referrer;
        }
        return Success("Welcome!");
    }

    public async Task<StatusMessage> ChangeEmailAddressAsync(Participant participant, string oldEmailAddress)
    {
        var oldEmailAddressOwner = await participants.FindAsync(oldEmailAddress);
        if (oldEmailAddressOwner is null)
        {
            return NoChange();
        }
        if (oldEmailAddressOwner.FutureEmailAddress?.Equals(participant.EmailAddress, StringComparison.OrdinalIgnoreCase) != true)
        {
            return Error("The link used to change emails is no longer valid.");
        }

        // Two cases:
        // 1. Migrate to a previously unused email address, in which case we must migrate everything.
        // 2. Migrate to an address that's in use, only possible through alias checking, in which case we must only migrate application group invitations.

        // So let's start with the common part:
        // migrate the group invites,
        var invitedGroups = await groups.Where(g => g.InvitedParticipants.Contains(oldEmailAddressOwner)).ToCollectionAsync();
        foreach (var invitedGroup in invitedGroups)
        {
            invitedGroup.InvitedParticipants.Remove(oldEmailAddressOwner);
            invitedGroup.InvitedParticipants.Add(participant);
        }
        // and delete the old account.
        participants.Remove(oldEmailAddressOwner);

        if (participant.Status == ParticipantStatus.Created)
        {
            var group = await groups.FirstOrDefaultAsync(g => g.Members.Contains(oldEmailAddressOwner));
            if (group is not null)
            {
                group.Members.Remove(oldEmailAddressOwner);
                group.Members.Add(participant);
            }

            var project = await projects.FirstOrDefaultAsync(p => p.Team.Contains(oldEmailAddressOwner));
            if (project is not null)
            {
                project.Team.Remove(oldEmailAddressOwner);
                project.Team.Add(participant);
            }

            var invitedProjects = await projects.Where(p => p.InvitedParticipants.Contains(oldEmailAddressOwner)).ToCollectionAsync();
            foreach (var invitedProject in invitedProjects)
            {
                invitedProject.InvitedParticipants.Remove(oldEmailAddressOwner);
                invitedProject.InvitedParticipants.Add(participant);
            }

            var submittedExpenses = await expenses.Where(e => e.Owners.Contains(oldEmailAddressOwner)).ToCollectionAsync();
            foreach (var expense in submittedExpenses)
            {
                expense.Owners.Remove(oldEmailAddressOwner);
                expense.Owners.Add(participant);
            }

            participant.Status = oldEmailAddressOwner.Status;
            participant.LastStatusReminderDate = oldEmailAddressOwner.LastStatusReminderDate;
            participant.Referrer = oldEmailAddressOwner.Referrer;
            participant.IsSoftRejected = oldEmailAddressOwner.IsSoftRejected;
            participant.GivenName = oldEmailAddressOwner.GivenName;
            participant.FamilyName = oldEmailAddressOwner.FamilyName;
            participant.Profile = oldEmailAddressOwner.Profile;
            participant.VisaInformation.PassportPhotoId = oldEmailAddressOwner.VisaInformation.PassportPhotoId;
            participant.VisaInformation.ParticipantDetails = oldEmailAddressOwner.VisaInformation.ParticipantDetails;
            participant.VisaInformation.Letter = oldEmailAddressOwner.VisaInformation.Letter;
            participant.TravelReimbursementTier = oldEmailAddressOwner.TravelReimbursementTier;
            participant.AdminRemarks = oldEmailAddressOwner.AdminRemarks;
            return Success($"You have successfully changed your email address to **{participant.EmailAddress}**.");
        }

        return Success("Welcome back!");
    }
}