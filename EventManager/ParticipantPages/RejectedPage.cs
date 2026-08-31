using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class RejectedPage : Page<Participant?>
{
    public override async Task<PageView> ViewAsync(Participant? participant) => participant?.Status switch
    {
        ParticipantStatus.Rejected => RequiredView("Rejected"),
        _ => ForbiddenView()
    };
}