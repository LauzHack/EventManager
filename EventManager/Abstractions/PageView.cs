using System.Collections.Immutable;

namespace EventManager.Abstractions;

/// <summary>
/// Contains information about a page's status and contents.
/// </summary>
public sealed record PageView
{
    /// <summary>
    /// The page this view is for.
    /// </summary>
    public Page Page { get; }

    /// <summary>
    /// The title of this view.
    /// Empty if it is not interactable and its summary is empty (but non-null for usage convenience; non-interactable pages should not be used anyway).
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Whether the page must be displayed to the user.
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// Whether the user can interact with the page.
    /// </summary>
    public bool IsInteractable { get; }

    /// <summary>
    /// The action verb used to describe editing the page, or null if interaction with the page should not be suggested.
    /// (The page may be interactable anyway, e.g., so that a link sent by email works)
    /// </summary>
    public string? Action { get; }

    /// <summary>
    /// The summary of this view.
    /// </summary>
    public ImmutableArray<PageSummaryItem> Summary { get; }

    private PageView(Page page, string title, bool isRequired, bool isInteractable, string? action, ImmutableArray<PageSummaryItem> summary)
    {
        Page = page;
        Title = title;
        IsRequired = isRequired;
        IsInteractable = isInteractable;
        Action = action;
        Summary = summary;
    }

    /// <summary>
    /// The page should not be used at all.
    /// </summary>
    public static PageView Forbidden(Page page)
        => new(page, "", false, false, null, []);

    /// <summary>
    /// The page cannot be interacted with, but its summary should be shown if it is not empty.
    /// </summary>
    public static PageView SummaryOnly(Page page, string title, ImmutableArray<PageSummaryItem> summary)
    {
        if (summary.Length == 0)
        {
            return Forbidden(page);
        }
        return new(page, title, false, false, null, summary);
    }

    /// <summary>
    /// The page can be interacted with, but does not need to be.
    /// </summary>
    public static PageView Editable(Page page, string title, string? action, ImmutableArray<PageSummaryItem> summary)
        => new(page, title, false, true, action, summary);

    /// <summary>
    /// The page must be interacted with before continuing to the next page.
    /// </summary>
    public static PageView Required(Page page, string title)
        => new(page, title, true, true, null, []);
}