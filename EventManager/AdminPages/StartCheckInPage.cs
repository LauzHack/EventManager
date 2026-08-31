using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class StartCheckInPage(ConfigValue<EventStatus> eventStatus) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (eventStatus.Value is EventStatus.ApplicationsClosed)
        {
            return RequiredView("Start check-in");
        }
        return ForbiddenView();
    }

    public async Task<StatusMessage> StartAsync(Admin admin)
    {
        if (!admin.IsOwner)
        {
            return Error("Only an owner can start check-in");
        }

        eventStatus.Set(EventStatus.CheckInStarted);
        return Success("Check-in has now started.");
    }
}