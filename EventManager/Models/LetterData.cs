namespace EventManager.Models;

/// <summary>
/// Data used by the system to display letters.
/// </summary>
/// <param name="Address">The address of the letter authors, i.e., the event organizers.</param>
/// <param name="CultureName">The name of the culture to use for formatting the letter date, e.g., `fr-CH`.</param>
/// <param name="Signee">The person signing the letter.</param>
/// <param name="SigneeContact">The contact information, such as email address and phone number, of the signee.</param>
/// <param name="SignatureFileId">The ID of the file containing the signee's signature.</param>
public sealed record LetterData(
    string Address,
    string CultureName,
    string Signee,
    string SigneeContact,
    string SignatureFileId
);