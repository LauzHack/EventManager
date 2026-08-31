using System.Collections.Generic;
using System.Linq;

namespace EventManager.Models;

/// <summary>
/// A challenge setter.
/// </summary>
public sealed class ChallengeSetter(string name, int order, bool isChallengeOptIn) : User
{
    public const int MaxDescriptionLength = 3000;

    /// <summary>
    /// Challenge setters are identified by their name.
    /// </summary>
    public override string Id => Name;

    /// <summary>
    /// Name of the challenge setter.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Order of the challenge setter, used to sort challenges and awards.
    /// </summary>
    public int Order { get; set; } = order;

    /// <summary>
    /// Whether the challenge has to be opted into by teams.
    /// </summary>
    public bool IsChallengeOptIn { get; set; } = isChallengeOptIn;

    /// <summary>
    /// Description of the challenge, if set.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The awards given by this challenge setter.
    /// </summary>
    public ISet<Award> Awards { get; } = new HashSet<Award>();

    /// <summary>
    /// Given a mapping of projects by their ID, and a builder of projects to their list of awards, adds this setter's awards to the builder.
    /// </summary>
    public void BuildAwardsMapping(IReadOnlyDictionary<string, Project> projectsById, OrderedDictionary<Project, IReadOnlyCollection<string>> awardsBuilder)
    {
        foreach (var award in Awards.OrderBy(a => a.Order))
        {
            string awardName = $"{Name} {award.Name}";
            if (projectsById.TryGetValue(award.ProjectId, out var project))
            {
                if (awardsBuilder.TryGetValue(project, out var projectAwards))
                {
                    awardsBuilder[project] = [.. projectAwards, awardName];
                }
                else
                {
                    awardsBuilder[project] = [awardName];
                }
            }
        }
    }
}