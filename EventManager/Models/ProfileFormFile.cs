using System.Collections.Immutable;

namespace EventManager.Models;

/// <summary>
/// Description of a file participants can or must upload.
/// </summary>
/// <param name="Name">The file name, uniquely identifying the file.</param>
/// <param name="Description">The file description, which may contain Markdown.</param>
/// <param name="IsRequired">Whether uploading the file is required.</param>
/// <param name="AllowedExtensions">The allowed file extensions, or empty if all are allowed. (This is only a hint for users, we do not trust file extensions)</param>
public sealed record ProfileFormFile(
    string Name,
    string Description,
    bool IsRequired,
    ImmutableArray<string> AllowedExtensions
);