namespace EventManager.Models;

/// <summary>
/// Prints participant statuses in a human-readable format
/// </summary>
public static class ParticipantStatusExtensions
{
    public static string ToDisplayString(this ParticipantStatus status) => status switch
    {
        ParticipantStatus.Rejected => "Rejected",
        ParticipantStatus.WithdrawnBeforeConfirmation => "Withdrawn before confirmation",
        ParticipantStatus.WithdrawnAfterConfirmation => "Withdrawn after confirmation",
        ParticipantStatus.DidNotConfirm => "Did not confirm in time",
        ParticipantStatus.Created or ParticipantStatus.EmailAddressVerified or ParticipantStatus.ProfileFilled => "Created",
        ParticipantStatus.Finalized => "Finalized",
        ParticipantStatus.Accepted => "Accepted, not confirmed yet",
        ParticipantStatus.Confirmed => "Confirmed, not checked in yet",
        ParticipantStatus.CheckedIn => "Checked in",
        ParticipantStatus.DeclaredTravelExpenses => "Declared travel expenses if needed",
        ParticipantStatus.Demoed => "Demoed",
        _ => "Other" // should not happen but we might forget a case...
    };
}