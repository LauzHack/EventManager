using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class EventLimitsPage(ConfigValue<EventLimits> eventLimits) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (eventLimits.Value is null)
        {
            return RequiredView("Event limits");
        }

        PageSummaryItem[] summary = [
            eventLimits.Value.ApplicationGroupSize <= 1 ? ("Applications", "Alone") : ("Application group size", eventLimits.Value.ApplicationGroupSize),
            eventLimits.Value.ProjectTeamSize == 0 ? ("Projects", "Disabled") : ("Project team size", eventLimits.Value.ProjectTeamSize),
            ("Days to confirm", eventLimits.Value.DaysToConfirm),
            ("Days between reminders", eventLimits.Value.DaysBetweenReminders)
        ];

        return admin.IsOwner ? EditableView("Event limits", "Edit", summary)
                             : SummaryOnlyView("Event limits", summary);
    }

    public async Task<StatusMessage> EditAsync(EventLimits limits)
    {
        if (limits.DaysToConfirm == 0)
        {
            return Error("Days to confirm cannot be zero.");
        }
        if (limits.DaysBetweenReminders == 0)
        {
            return Error("Days between reminders cannot be zero.");
        }

        eventLimits.Set(limits);
        return Success("Event limits updated.");
    }
}