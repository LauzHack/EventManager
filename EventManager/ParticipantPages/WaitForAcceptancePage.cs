using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class WaitForAcceptancePage(DbValues<ApplicationGroup> groups,
                                          EventDetails details, EventLimits limits,
                                          EmailSender emailSender) : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant) => participant.Status switch
    {
        ParticipantStatus.Finalized or ParticipantStatus.Accepted => RequiredView("Application finalized"),
        ParticipantStatus.Confirmed => EditableViaLinkView("Application finalized"), // ok if confirmed, so the email link still works
        _ => ForbiddenView()
    };

    public async Task<StatusMessage> ConfirmAsync(Participant participant)
    {
        if (participant.Status >= ParticipantStatus.Confirmed)
        {
            return NoChange();
        }

        if (participant.Status == ParticipantStatus.Accepted)
        {
            await emailSender.SendEmailAsync(
                recipient: participant.EmailAddress,
                subject: "Thank you for confirming",
                body: $"We look forward to seeing you at {details}!\n\n"
                    + details.ConfirmationText
                    + ((limits.ApplicationGroupSize <= 1 || limits.ProjectTeamSize <= 1) ? "" : ("\n\n"
                        + "**You can form any team you want at the event, but changing your application group is no longer possible.** "
                        + "If someone else would like to come to the event with you, they should apply separately. "
                        + "This also applies if you applied with someone and they would like to be replaced by someone else, for fairness reasons.")),
                operation: null,
                attachedEvent: details
            );
            participant.Status = ParticipantStatus.Confirmed;
            return Success("Thanks for confirming, and see you soon!");
        }

        return Error("The link you clicked on is no longer valid.");
    }

    public async Task<StatusMessage> UnfinalizeAsync(Participant participant)
    {
        if (participant.Status != ParticipantStatus.Finalized)
        {
            return NoChange();
        }

        var group = await groups.FirstAsync(g => g.Members.Contains(participant));

        var emails = group.Members.Select(member => new Email(
            Recipient: member.EmailAddress,
            Subject: "Application un-finalized",
            Body: group.Members.Count > 1
                ? ($"{participant.FullName} un-finalized the application.\n\n"
                 + "**The application will not be considered unless a group member finalizes it again**. "
                 + "Please make any necessary changes to your application, then finalize it again.")
                : ("You un-finalized your application.\n\n"
                 + "**Your application will not be considered unless you finalize it again**. "
                 + "Please make any necessary changes to your application, then finalize it again."),
            Operation: Operation.CreatePageView<Participant>()
        )).ToArray();

        foreach (var member in group.Members)
        {
            member.Status = ParticipantStatus.Finalized - 1;
        }

        await emailSender.SendAsync(emails);

        return ImportantInformation("You have unfinalized your application. **Please make any necessary changes, then finalize it again**.");
    }

    public async Task<StatusMessage> WithdrawAsync(Participant participant)
    {
        // If the participant is alone in their group, that's fine, but otherwise, the rest of the group can now be accepted without them
        var group = await groups.FirstAsync(g => g.Members.Contains(participant));
        if (group.Members.Count > 1)
        {
            group.Members.Remove(participant);
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
}