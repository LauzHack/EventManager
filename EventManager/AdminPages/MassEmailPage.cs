using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class MassEmailPage(DbValues<Participant> participants, EmailSender emailSender) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (admin.IsOwner)
        {
            return EditableView("Mass email", "Send");
        }
        return ForbiddenView();
    }

    public async Task<StatusMessage> SendToParticipantsAsync(ParticipantStatus minStatus, ParticipantStatus maxStatus, string subject, string body, bool includeViewOperation)
    {
        if (minStatus > maxStatus)
        {
            return Error($"Impossible range: minimum {minStatus} comes after maximum {maxStatus}");
        }

        var selectedEmailAddresses = await participants.Where(p => p.Status >= minStatus && p.Status <= maxStatus)
                                                       .Select(p => p.EmailAddress)
                                                       .ToCollectionAsync();

        if (includeViewOperation)
        {
            var emails = selectedEmailAddresses.Select(emailAddress => new Email(
                Recipient: emailAddress,
                Subject: subject,
                Body: body,
                Operation: Operation.CreatePageView<Participant>(),
                OperationDescription: "Log in"
            )).ToArray();
            await emailSender.SendAsync(emails);
        }
        else
        {
            // This is typically much more efficient
            await emailSender.SendCopyAsync(subject, body, selectedEmailAddresses);
        }

        return Success($"Sent {selectedEmailAddresses.Count} emails.");
    }

    public async Task<StatusMessage> SendToEmailAddressesAsync(string[] emailAddresses, string subject, string body, bool includeApplicationLink, string? applicationReferrer)
    {
        if (applicationReferrer is not null && !includeApplicationLink)
        {
            return Error("Cannot have a referrer without sending a link.");
        }

        var deduplicatedEmailAddresses = emailAddresses.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (includeApplicationLink)
        {
            var op = Operation.CreatePageView<Participant>();
            if (applicationReferrer is not null)
            {
                op = op.WithExtraTextArgument("utm_source", applicationReferrer);
            }
            await emailSender.SendCopyAsync(subject, body, deduplicatedEmailAddresses, op, "Apply");
        }
        else
        {
            await emailSender.SendCopyAsync(subject, body, deduplicatedEmailAddresses);
        }

        var deduplicatedMessage = emailAddresses.Length == deduplicatedEmailAddresses.Length
                                ? ""
                                : $" ({(emailAddresses.Length - deduplicatedEmailAddresses.Length).ToString(CultureInfo.InvariantCulture)} addresses were duplicates)";
        return Success($"Sent {deduplicatedEmailAddresses.Length} emails.{deduplicatedMessage}");
    }
}