using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class WelcomePage : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant)
        => RequiredView("Welcome!");
}