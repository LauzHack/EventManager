using System.Collections.Generic;

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
}