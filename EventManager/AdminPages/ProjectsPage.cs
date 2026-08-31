using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class ProjectsPage(DbValues<Project> projects, EventStatus eventStatus, EventLimits eventLimits) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (eventLimits.ProjectTeamSize == 0)
        {
            return ForbiddenView();
        }
        return EditableView("Projects", "View");
    }

    public override async Task<object?> GetModelAsync(Admin admin)
        => await projects.OrderBy(p => p.Title).ToCollectionAsync();

    public async Task<StatusMessage> MarkAsDemoedAsync(string id)
    {
        if (eventStatus is not EventStatus.JudgingStarted)
        {
            return Error("Demo status can only be set once judging has started.");
        }
        var project = await projects.FindAsync(id);
        if (project is null)
        {
            return Error("This project no longer exists.");
        }
        // should be impossible since projects are created after checkin, but just making sure
        if (project.Team.Any(p => p.Status < ParticipantStatus.CheckedIn))
        {
            return Error($"Members of the **{project.Title}** project group cannot be marked as demoed as not all of them have checked in yet.");
        }
        if (project.Team.Any(p => p.Status >= ParticipantStatus.Demoed))
        {
            return Error($"Members of the **{project.Title}** project are already marked as demoed.");
        }

        foreach (var member in project.Team)
        {
            member.Status = ParticipantStatus.Demoed;
        }
        return Success($"Members of the **{project.Title}** project are now marked as demoed.");
    }

    public async Task<StatusMessage> MarkAsNotDemoedAsync(string id)
    {
        if (eventStatus is not EventStatus.JudgingStarted)
        {
            return Error("Demo status can only be set once judging has started.");
        }
        var project = await projects.FindAsync(id);
        if (project is null)
        {
            return Error("This project no longer exists.");
        }
        if (project.Team.Any(p => p.Status != ParticipantStatus.Demoed))
        {
            return Error($"Members of the **{project.Title}** project are already not marked as demoed.");
        }

        foreach (var member in project.Team)
        {
            member.Status = ParticipantStatus.DeclaredTravelExpenses;
        }
        return Success($"Members of the **{project.Title}** project are now marked as **not** demoed.");
    }
}