namespace EventManager.Models;

/// <summary>
/// Information related to visa for a participant, for participants who need an invitation letter.
/// </summary>
public sealed class ParticipantVisaInformation
{
    /// <summary>
    /// ID of the file uploaded by the participant representing their passport photo, if any. 
    /// </summary>
    public string? PassportPhotoId { get; set; }

    /// <summary>
    /// Details provided by the participant, if any.
    /// </summary>
    public string[] ParticipantDetails { get; set; } = [];

    /// <summary>
    /// Details defined by an administrator, if any.
    /// </summary>
    public string? AdminDetails { get; set; }

    /// <summary>
    /// Visa invitation letter created by an administrator for the user, if any.
    /// </summary>
    public Letter? Letter { get; set; }
}