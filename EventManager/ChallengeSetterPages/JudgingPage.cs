using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ChallengeSetterPages;

public sealed class JudgingPage(DbValues<Project> projects, EventStatus eventStatus) : Page<ChallengeSetter>
{
    public override async Task<PageView> ViewAsync(ChallengeSetter setter)
        => RequiredView(eventStatus >= EventStatus.JudgingStarted ? "Judging" : "Submissions");

    public override async Task<object?> GetModelAsync(ChallengeSetter setter)
    {
        var filteredProjects = setter.IsChallengeOptIn ? projects.Where(p => p.Challenges.Contains(setter.Name))
                                                       : projects;
        return await filteredProjects.OrderBy(p => p.Title).ToCollectionAsync();
    }

    public async Task<StatusMessage> EditAsync(ChallengeSetter setter, AwardDefinition[] awards)
    {
        setter.Awards.Clear();

        int order = 0;
        foreach (var entry in awards)
        {
            setter.Awards.Add(new(order, entry.Name, entry.ProjectId));
            order += 1;
        }

        return Success("Awards edited.");
    }

    public sealed record AwardDefinition(string Name, string ProjectId);
}