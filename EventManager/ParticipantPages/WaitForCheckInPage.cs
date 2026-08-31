using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class WaitForCheckInPage() : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant)
    {
        if (participant.Status < ParticipantStatus.CheckedIn)
        {
            return RequiredView("Go to check in");
        }
        return ForbiddenView();
    }
}