using System;
using System.Collections.Generic;

namespace EventManager.Models;

/// <summary>
/// A group of participants applying together.
/// </summary>
/// <remarks>
/// Invariant: All finalized participants are in an application group, even if they're applying alone.
/// Participants who have not finalized yet may, but do not have to, be in an application group.
/// </remarks>
public sealed class ApplicationGroup(string id)
{
    /// <summary>
    /// The group's ID.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// The members currently in the group.
    /// </summary>
    public ISet<Participant> Members { get; } = new HashSet<Participant>();

    /// <summary>
    /// The email addresses of the people invited to the group.
    /// </summary>
    public ISet<Participant> InvitedParticipants { get; } = new HashSet<Participant>();

    /// <summary>
    /// The date and time at which the group's members moved to the "finalized" status.
    /// Meaningless if that is not the case.
    /// </summary>
    public DateTimeOffset FinalizationDate { get; set; }

    /// <summary>
    /// The date and time at which the group's members moved to the "accepted" status.
    /// Meaningless if that is not the case.
    /// </summary>
    public DateTimeOffset AcceptanceDate { get; set; }
}