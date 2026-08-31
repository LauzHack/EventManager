using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class AliasPage(EmailSender emailSender) : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant)
    {
        if (participant.PossibleAliasEmailAddresses.Count == 0)
        {
            return ForbiddenView();
        }
        return RequiredView("Do we know you?");
    }

    public async Task<StatusMessage> ContinueAsync(Participant participant)
    {
        participant.PossibleAliasEmailAddresses = [];
        return Success("Thanks for double checking!");
    }

    public async Task<StatusMessage> ChooseCandidateAsync(Participant participant, string emailAddress)
    {
        var matchingCandidate = participant.PossibleAliasEmailAddresses.FirstOrDefault(a => a.Equals(emailAddress, StringComparison.OrdinalIgnoreCase));
        if (matchingCandidate is null)
        {
            return Error($"The email **{emailAddress}** does not match any of the candidates.");
        }

        participant.FutureEmailAddress = emailAddress;
        await emailSender.SendEmailAsync(
            recipient: emailAddress,
            subject: "Migrate to an existing account",
            body: $"Please confirm the migration of {participant.EmailAddress} to {emailAddress}, including any pending group invitations."
                + "_You may use this link again afterwards to log in with this new email address._",
            operation: Operation.CreatePageAction<Participant?, EmailPage>(nameof(EmailPage.ChangeEmailAddressAsync), ("oldEmailAddress", participant.EmailAddress)),
            operationDescription: "Confirm"
        );
        return ImportantInformation($"Please confirm the migration via the email sent to **{emailAddress}**.");
    }
}