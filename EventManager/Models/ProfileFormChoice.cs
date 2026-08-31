using System;
using System.Collections.Immutable;

namespace EventManager.Models;

/// <summary>
/// Choice participants can or must make.
/// </summary>
/// <param name="Name">The choice name, uniquely identifying the choice.</param>
/// <param name="Description">The choice description, which may contain Markdown.</param>
/// <param name="IsRequired">Whether making the choice is required.</param>
/// <param name="Options">The preset options participants can choose from.</param>
/// <param name="AllowsCustomOption">Whether participants can enter a custom option.</param>
/// <param name="CustomOptionSuggestions">A list of suggestions for custom options, ignored if <see cref="AllowsCustomOption" /> is false.</param>
/// <remarks>
/// The difference between "option" and "custom option suggestion" is that the former are always visible,
/// while the latter are only displayed if the participant starts typing an "other" choice.
/// </remarks>
public sealed record ProfileFormChoice(
    string Name,
    string Description,
    bool IsRequired,
    ImmutableArray<string> Options,
    bool AllowsCustomOption,
    ImmutableArray<string> CustomOptionSuggestions
)
{
    /// <summary>
    /// Maximum length for custom options.
    /// Mainly exists to ensure event organizers don't have to read walls of text
    /// </summary>
    public const int MaxCustomOptionLength = 500;

    /// <summary>
    /// Whether this choice is required without any actual choice, e.g., for "I have read the rules".
    /// </summary>
    public bool IsRequiredSingleOption
        => IsRequired && Options.Length == 1 && !AllowsCustomOption;

    /// <summary>
    /// Checks whether the given answer is acceptable for this choice.
    /// </summary>
    public bool IsAcceptableAnswer(string answer)
        => !string.IsNullOrWhiteSpace(answer)
        && answer.Length <= MaxCustomOptionLength
        && (AllowsCustomOption || Options.Contains(answer, StringComparer.Ordinal));
}