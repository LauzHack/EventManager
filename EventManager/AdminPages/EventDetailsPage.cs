using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class EventDetailsPage(ConfigValue<EventDetails> eventDetails) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (eventDetails.Value is null)
        {
            return RequiredView("Event details");
        }

        PageSummaryItem[] summary = [
            ("Title", eventDetails.Value.Title),
            ("Location", eventDetails.Value.Location),
            ("Duration", $"{eventDetails.Value.ToDateString()} ({eventDetails.Value.TimeZone})")
        ];

        return admin.IsOwner ? EditableView("Event details", "Edit", summary)
                             : SummaryOnlyView("Event details", summary);
    }

    public async Task<StatusMessage> EditAsync(EventDetails details)
    {
        if (details.End < details.Start)
        {
            return Error("The event cannot end before it begins.");
        }

        eventDetails.Set(details);
        return Success("Event details updated.");
    }
}