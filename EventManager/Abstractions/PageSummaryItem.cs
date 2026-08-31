using System.Globalization;

namespace EventManager.Abstractions;

/// <summary>
/// Item displayed as part of a page's summary.
/// </summary>
/// <param name="Label">The item label.</param>
/// <param name="Text">The item text.</param>
public sealed record PageSummaryItem(string Label, string Text)
{
    /// <summary>
    /// Convenience conversion operator to turn a tuple into a summary item.
    /// </summary>
    public static implicit operator PageSummaryItem((string, int) tuple)
        => new(tuple.Item1, tuple.Item2.ToString(format: null, CultureInfo.InvariantCulture));

    /// <summary>
    /// Convenience conversion operator to turn a tuple into a summary item.
    /// </summary>
    public static implicit operator PageSummaryItem((string, uint) tuple)
        => new(tuple.Item1, tuple.Item2.ToString(format: null, CultureInfo.InvariantCulture));

    /// <summary>
    /// Convenience conversion operator to turn a tuple into a summary item.
    /// </summary>
    public static implicit operator PageSummaryItem((string, string) tuple)
        => new(tuple.Item1, tuple.Item2);
}