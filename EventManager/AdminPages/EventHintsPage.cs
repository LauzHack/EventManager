using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class EventHintsPage(ConfigValue<EventHints> eventHints) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
        => EditableView("Process hints", "Edit");

    public async Task<StatusMessage> EditAsync(EventHints hints)
    {
        eventHints.Set(hints);
        return Success("Event hints updated.");
    }
}