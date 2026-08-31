using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class ChallengesPage(DbValues<ChallengeSetter> challengeSetters, DbValues<Project> projects, EventLimits limits) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin user)
        => limits.ProjectTeamSize == 0
         ? ForbiddenView()
         : EditableView("Challenges and awards", "Manage");

    public override async Task<object?> GetModelAsync(Admin user)
    {
        var setters = await challengeSetters.OrderBy(c => c.Order).ToCollectionAsync();
        var projectsById = await projects.ToDictionaryAsync(p => p.Id, StringComparer.Ordinal);
        var model = new List<ChallengeSetterAndProjects>();
        foreach (var setter in setters)
        {
            var awards = new OrderedDictionary<Project, IReadOnlyCollection<string>>();
            setter.BuildAwardsMapping(projectsById, awards);
            // Also add the projects without an award
            var others = projectsById.Values.Where(p => !setter.IsChallengeOptIn || p.Challenges.Contains(setter.Name)).OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase);
            foreach (var project in others)
            {
                awards.TryAdd(project, []);
            }
            model.Add(new(setter, awards));
        }
        return model;
    }

    public async Task<StatusMessage> EditAsync(ChallengeSetterDefinition[] setters)
    {
        if (setters.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Skip(1).Any()))
        {
            return Error("Cannot have multiple challenge setters with the same name.");
        }

        var resultDescription = new List<string>();
        var existing = await challengeSetters.ToCollectionAsync();
        foreach (var ex in existing)
        {
            var match = setters.FirstOrDefault(s => s.Name.Equals(ex.Name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                challengeSetters.Remove(ex);
                resultDescription.Add($"- **Removed {ex.Name}**");
            }
            else
            {
                int matchOrder = setters.IndexOf(match);
                if (ex.Order != matchOrder)
                {
                    resultDescription.Add($"- Moved {ex.Name}");
                }
                ex.Order = matchOrder;
                if (ex.IsChallengeOptIn != match.IsChallengeOptIn)
                {
                    resultDescription.Add($"- Made {ex.Name} {(match.IsChallengeOptIn ? "opt-in" : "opt-out")}");
                }
                ex.IsChallengeOptIn = match.IsChallengeOptIn;
            }
        }
        int order = 0;
        foreach (var setter in setters)
        {
            if (!existing.Any(e => e.Name.Equals(setter.Name, StringComparison.OrdinalIgnoreCase)))
            {
                challengeSetters.Add(new(setter.Name, order, setter.IsChallengeOptIn));
                resultDescription.Add($"- Added {setter.Name}");
            }
            order += 1;
        }

        if (resultDescription.Count == 0)
        {
            // Explicit so admins know they didn't change anything
            return Success("No changes.");
        }

        return Success("Edited challenge setters:\n" + string.Join('\n', resultDescription.Order(StringComparer.OrdinalIgnoreCase)));
    }

    public sealed record ChallengeSetterDefinition(string Name, bool IsChallengeOptIn);

    public sealed record ChallengeSetterAndProjects(ChallengeSetter ChallengeSetter, IReadOnlyDictionary<Project, IReadOnlyCollection<string>> Projects);
}