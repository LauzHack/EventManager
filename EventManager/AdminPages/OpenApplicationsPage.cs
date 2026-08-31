using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class OpenApplicationsPage(ConfigValue<EventStatus> eventStatus) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (eventStatus.Value is EventStatus.Configuring)
        {
            return RequiredView("Open applications");
        }
        return ForbiddenView();
    }

    public async Task<StatusMessage> OpenAsync(Admin admin)
    {
        if (!admin.IsOwner)
        {
            return Error("Only an owner can open applications.");
        }

        eventStatus.Set(EventStatus.ApplicationsOpen);
        return Success("Applications are now open.");
    }
}