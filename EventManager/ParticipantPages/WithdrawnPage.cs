using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class WithdrawnPage : Page<Participant?>
{
    public override async Task<PageView> ViewAsync(Participant? participant) => participant?.Status switch
    {
        ParticipantStatus.WithdrawnBeforeConfirmation or ParticipantStatus.WithdrawnAfterConfirmation => RequiredView("Withdrawal"),
        _ => ForbiddenView()
    };

    public async Task<StatusMessage> UndoAsync(Participant participant)
    {
        if (participant.Status == ParticipantStatus.WithdrawnBeforeConfirmation)
        {
            participant.Status = ParticipantStatus.EmailAddressVerified;
            return ImportantInformation("Welcome back! You may now edit your application, and **you must finalize it once you're ready**.");
        }

        if (participant.Status == ParticipantStatus.WithdrawnAfterConfirmation)
        {
            participant.Status = ParticipantStatus.Confirmed;
            return Success("Welcome back! Your participation is once again confirmed, see you at the event!");
        }

        return NoChange();
    }
}