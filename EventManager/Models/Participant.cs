using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace EventManager.Models;

/// <summary>
/// A participant.
/// </summary>
public sealed class Participant(string emailAddress) : User
{
    /// <summary>
    /// Participants are identified by their email address.
    /// </summary>
    public override string Id => EmailAddress;

    /// <summary>
    /// The participant's email address, which uniquely identifies them.
    /// </summary>
    public string EmailAddress { get; private set; } = emailAddress;

    /// <summary>
    /// The status of the participant's application.
    /// </summary>
    public ParticipantStatus Status { get; set; }

    /// <summary>
    /// The date and time at which the participant was last reminded of their status.
    /// Used by periodic reminder tasks.
    /// </summary>
    public DateTimeOffset? LastStatusReminderDate { get; set; }

    /// <summary>
    /// The email address the participant wants to change _to_.
    /// Used to ensure "change email" links cannot be forged.
    /// </summary>
    public string? FutureEmailAddress { get; set; }

    /// <summary>
    /// The entity that referred this participant, if any.
    /// This can be used to, e.g., track how successful an advertising campaign on a specific website was.
    /// </summary>
    public string? Referrer { get; set; }

    /// <summary>
    /// The possible aliases of this participant, given their name.
    /// Empty if the participant either has no possible aliases or has explicitly confirmed none of the proposed aliases were a match.
    /// </summary>
    public IReadOnlyCollection<string> PossibleAliasEmailAddresses { get; set; } = [];

    /// <summary>
    /// Whether the user has been "soft-rejected", i.e., cannot be accepted but does not know it until all non-accepted participants are rejected.
    /// Invariant: If this property is true, then the user's status cannot be "accepted" or anything above that.
    /// </summary>
    public bool IsSoftRejected { get; set; }

    /// <summary>
    /// The participant's given name, if it has been filled in.
    /// </summary>
    public string? GivenName { get; set; }

    /// <summary>
    /// The participant's family name, if they have one.
    /// </summary>
    public string? FamilyName { get; set; }

    /// <summary>
    /// The participant's full name, if at least their given name has been filled in.
    /// </summary>
    public string? FullName
    {
        get
        {
            if (GivenName == null)
            {
                return null;
            }
            if (FamilyName == null)
            {
                return GivenName;
            }
            // English ordering, gotta pick one since we don't support localization for now anyway
            return GivenName + " " + FamilyName;
        }
    }

    /// <summary>
    /// Profile attributes associated with the participant.
    /// </summary>
    public ImmutableDictionary<string, string> Profile { get; set; } = [];

    /// <summary>
    /// Information related to the participant's visa information letter.
    /// </summary>
    public ParticipantVisaInformation VisaInformation { get; set; } = new();

    /// <summary>
    /// The tier the participant is in for travel reimbursement, if travel reimbursement is enabled and the participant has requested it.
    /// </summary>
    public string? TravelReimbursementTier { get; set; }

    /// <summary>
    /// Remarks set by administrators about the participant.
    /// </summary>
    public string? AdminRemarks { get; set; }
}