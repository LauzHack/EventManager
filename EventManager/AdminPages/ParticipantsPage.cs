using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

namespace EventManager.AdminPages;

public sealed class ParticipantsPage(DbValues<Participant> participants, EmailSender emailSender) : Page<Admin>
{
    public override bool RedisplayAfterAction
        => true;

    public override async Task<PageView> ViewAsync(Admin admin)
    {
        // Do the part that can be done in a single DB query first,
        var countsByStatus = await participants.GroupBy(g => g.Status)
                                               .OrderBy(g => g.Key)
                                               .Select(g => new { Status = g.Key, Count = g.Count() })
                                               .ToCollectionAsync();
        // then do the local processing on those results.
        var summary = countsByStatus.Where(g => g.Count > 0)
                                    .GroupBy(g => g.Status.ToDisplayString(), StringComparer.Ordinal)
                                    .Select(g2 => (g2.Key, g2.Sum(g => g.Count)))
                                    .ToArray();

        return EditableView("Participants", "Manage", [.. summary]);
    }

    public override async Task<object?> GetModelAsync(Admin admin)
        => await participants.OrderByName().ToCollectionAsync();

    public async Task<StatusMessage> ChangeEmailAddressAsync(string oldEmailAddress, string newEmailAddress)
    {
        if (oldEmailAddress.Equals(newEmailAddress, StringComparison.OrdinalIgnoreCase))
        {
            return NoChange();
        }

        var participant = await participants.FindAsync(oldEmailAddress);
        if (participant is null)
        {
            return Error($"No participant with email **{oldEmailAddress}**.");
        }

        var existingNew = await participants.FindAsync(newEmailAddress);
        if (existingNew is not null)
        {
            return Error($"Cannot migrate to **{newEmailAddress}** as it is in use already.");
        }

        participant.FutureEmailAddress = newEmailAddress;
        await emailSender.SendEmailAsync(
            recipient: newEmailAddress,
            subject: "Email change",
            body: $"Please confirm the admin-initiated migration of {oldEmailAddress} to {newEmailAddress}."
                + "_You may use this link again afterwards to log in with this new email address._",
            operation: Operation.CreatePageAction<Participant?, EmailPage>(nameof(EmailPage.ChangeEmailAddressAsync), ("oldEmailAddress", participant.EmailAddress)),
            operationDescription: "Confirm"
        );
        return Success($"Sent an email to **{newEmailAddress}** to confirm this migration.");
    }

    public async Task<StatusMessage> SetRemarksAsync(string emailAddress, string? remarks)
    {
        var participant = participants.FirstOrDefault(p => p.EmailAddress == emailAddress);
        if (participant is null)
        {
            return Error($"No participant with email **{emailAddress}**.");
        }

        participant.AdminRemarks = remarks;

        if (remarks is null)
        {
            return Success($"Cleared remarks for **{participant.FullName ?? participant.EmailAddress}**.");
        }
        return Success($"Set remarks for **{participant.FullName ?? participant.EmailAddress}** to '{remarks}'.");
    }

    public async Task<StatusMessage> SetSoftRejectionAsync(string[] emailAddresses)
    {
        var allParticipants = await participants.ToCollectionAsync();
        var participantsByEmailAddress = allParticipants.ToDictionary(p => p.EmailAddress, StringComparer.Ordinal);

        foreach (var emailAddress in emailAddresses)
        {
            participantsByEmailAddress[emailAddress].IsSoftRejected = true;
            participantsByEmailAddress.Remove(emailAddress);
        }
        foreach (var remaining in participantsByEmailAddress.Values)
        {
            remaining.IsSoftRejected = false;
        }

        return Success("Soft rejection status updated.");
    }
}