namespace EventManager.Models;

/// <summary>
/// Limits related to the event.
/// </summary>
/// <param name="ApplicationGroupSize">The maximal size of application groups, where 0 or 1 means no application groups.</param>
/// <param name="ProjectTeamSize">The maximal size of project teams, where 0 means no projects.</param>
/// <param name="DaysToConfirm">The number of days users have to confirm once accepted.</param>
/// <param name="DaysBetweenReminders">The number of days betweeen emails to remind users to finalize their application or confirm their acceptance.</param>
public sealed record EventLimits(
    uint ApplicationGroupSize,
    uint ProjectTeamSize,
    uint DaysToConfirm,
    uint DaysBetweenReminders
);