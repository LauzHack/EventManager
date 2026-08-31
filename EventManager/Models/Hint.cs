namespace EventManager.Models;

/// <summary>
/// Hint about a process, such as applying or presenting a project, intended to help participants know what to expect.
/// </summary>
/// <param name="Emoji">Emoji to display as the step icon.</param>
/// <param name="FirstLine">First line of Markdown text.</param>
/// <param name="SecondLine">Second line of Markdown text.</param>
public sealed record Hint(string Emoji, string FirstLine, string SecondLine)
{
    public string FullDescription
        => FirstLine + (SecondLine is "" ? "" : $"  \n{SecondLine}");
}