namespace EventManager.Models;

/// <summary>
/// The status of a participant's event application.
/// </summary>
/// <remarks>
/// These are ordered to make comparisons easy: anything after Accepted is "logically more" than Accepted,
/// so one can do ">= Accepted" to also check for Confirmed, CheckedIn, etc.
/// </remarks>
public enum ParticipantStatus
{
    /// <summary>
    /// The participant was explicitly rejected from the event.
    /// </summary>
    Rejected = -4,

    /// <summary>
    /// The participant did not confirm their acceptance in time and was thus rejected from the event.
    /// </summary>
    DidNotConfirm = -3,

    /// <summary>
    /// The participant withdrew their application before being accepted and confirmed.
    /// </summary>
    WithdrawnBeforeConfirmation = -2,

    /// <summary>
    /// The participant withdrew their application after being accepted and confirmed.
    /// </summary>
    WithdrawnAfterConfirmation = -1,

    /// <summary>
    /// The participant created their application, but their email address may not be verified.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The participant's email address was verified, i.e., they can receive emails sent to it, but they have not filled their profile yet.
    /// </summary>
    EmailAddressVerified = 1,

    /// <summary>
    /// The participant filled their profile, but did not finalize their application yet.
    /// </summary>
    ProfileFilled = 2,

    /// <summary>
    /// The participant completed and finalized their application, and is now waiting for a decision.
    /// </summary>
    Finalized = 3,

    /// <summary>
    /// The participant was accepted to the event.
    /// </summary>
    Accepted = 4,

    /// <summary>
    /// The participant confirmed their acceptance and is now expected at the event.
    /// </summary>
    Confirmed = 5,

    /// <summary>
    /// The participant checked in at the event.
    /// </summary>
    CheckedIn = 6,

    /// <summary>
    /// The participant declared their travel expenses, if any.
    /// </summary>
    DeclaredTravelExpenses = 7,

    /// <summary>
    /// The participant demoed their project.
    /// </summary>
    Demoed = 8
}