using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace EventManager.Models;

/// <summary>
/// Profile form that participants must fill.
/// </summary>
/// <param name="Choices">Choices participants can or must make.</param>
/// <param name="Files">Files participants can or must upload.</param>
public sealed record ProfileForm(ImmutableArray<ProfileFormChoice> Choices, ImmutableArray<ProfileFormFile> Files)
{
    /// <summary>
    /// Whether participants have no choices to make nor files to upload.
    /// </summary>
    public bool IsEmpty
        => Choices.Length == 0 && Files.Length == 0;

    /// <summary>
    /// Gets the "free" choices participants can make, i.e., those that are not simply agreeing with a single option without custom options.
    /// </summary>
    public IEnumerable<ProfileFormChoice> FreeChoices
        => Choices.Where(c => c.AllowsCustomOption || c.Options.Length > 1);
}