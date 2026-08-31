using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class DisabledPage(DbValues<Participant> participants, EventStatus eventStatus, EmailSender emailSender) : Page<Participant?>
{
    public override async Task<PageView> ViewAsync(Participant? participant)
    {
        if (participant?.Status >= ParticipantStatus.Accepted || participant?.Status == ParticipantStatus.WithdrawnAfterConfirmation)
        {
            return ForbiddenView();
        }
        if (eventStatus == EventStatus.ApplicationsOpen)
        {
            return ForbiddenView();
        }
        return RequiredView("Applications are not open");
    }

    public async Task<StatusMessage> LogInAsync(string emailAddress)
    {
        // We really don't want to create new participants at this stage!
        var participant = await participants.FindAsync(emailAddress);
        if (participant is null)
        {
            return Error($"The email **{emailAddress}** does not correspond to a participant.");
        }
        if (participant.Status is not (>= ParticipantStatus.Accepted or ParticipantStatus.WithdrawnAfterConfirmation))
        {
            return Error($"The participant **{participant.FullName}** has not been accepted to the event.");
        }

        await emailSender.SendEmailAsync(
            recipient: emailAddress,
            subject: "Log in",
            body: "Use the link below to log in.",
            operation: Operation.CreatePageView<Participant>()
        );
        return Success($"Please log in via the email sent to **{emailAddress}**.");
    }
}