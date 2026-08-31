using System;
using System.Collections.Immutable;

namespace EventManager.Models;

/// <summary>
/// Format of visa invitation letters.
/// </summary>
/// <param name="Template">The template, using placeholders for some properties.</param>
/// <param name="ParticipantDetails">The details participants should provide to obtain a visa invitation letter.</param>
/// <param name="AdminDetails">The details administrators should provide to generate a visa invitation letter.</param>
public sealed record VisaInvitationFormat(string Template, ImmutableArray<string> ParticipantDetails, string AdminDetails)
{
    /// <summary>
    /// Placeholder used in <see cref="Template" /> for the participant name.
    /// </summary>
    public const string TemplateNamePlaceholder = "$NAME";

    /// <summary>
    /// Placeholder used in <see cref="Template" /> for the participant details.
    /// </summary>
    public const string TemplateDetailsPlaceholder = "$DETAILS";

    /// <summary>
    /// Whether the template contains both placeholders.
    /// </summary>
    public bool ContainsPlaceholders
        => Template.Contains(TemplateNamePlaceholder, StringComparison.Ordinal) && Template.Contains(TemplateDetailsPlaceholder, StringComparison.Ordinal);

    /// <summary>
    /// Creates the body of a letter based on this format, using the given name and details.
    /// </summary>
    public string CreateLetterBody(string name, string details)
        => Template.Replace(TemplateNamePlaceholder, name, StringComparison.Ordinal)
                   .Replace(TemplateDetailsPlaceholder, details, StringComparison.Ordinal);
}