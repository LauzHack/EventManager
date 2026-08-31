namespace EventManager.Web;

using Markdig;

/// <summary>
/// Overtakes Markdig's "Markdown" utility class name and ensures we aren't vulnerable to HTML injection.
/// </summary>
public static class Markdown
{
    private static readonly MarkdownPipeline _noRawHtmlPipeline
        = new MarkdownPipelineBuilder().DisableHtml().Build();

    public static string ToHtml(string markdown)
        => Markdig.Markdown.ToHtml(markdown, _noRawHtmlPipeline);
}