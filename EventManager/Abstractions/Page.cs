using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

/// <summary>
/// Infrastructure, use the derived type instead.
/// A page shown to users.
/// </summary>
public abstract class Page
{
    /// <summary>
    /// The type of user the page accepts.
    /// </summary>
    public abstract Type UserType { get; }

    /// <summary>
    /// Whether a user is required to interact with the page.
    /// </summary>
    public abstract bool UserIsRequired { get; }

    /// <summary>
    /// Gets a view to display for the page.
    /// </summary>
    internal abstract Task<PageView> ViewAsync(object? user);

    /// <summary>
    /// Gets a model to use when displaying the page.
    /// </summary>
    internal abstract Task<object?> GetModelAsync(object? user);
}

/// <summary>
/// A page shown to users of a specific type.
/// The type can be nullable to indicate a user is not required.
/// </summary>
/// <typeparam name="TUser">The user type.</typeparam>
public abstract class Page<TUser> : Page
{
    private static readonly NullabilityInfoContext _nullabilityContext = new();

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    public sealed override Type UserType
        => typeof(TUser);

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    public sealed override bool UserIsRequired
        => _nullabilityContext.Create(GetType().GetMethod(nameof(ViewAsync), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!.GetParameters()[0]).ReadState == NullabilityState.NotNull;

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    internal sealed override Task<PageView> ViewAsync(object? user)
        => ViewAsync((TUser)user!);

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    internal sealed override Task<object?> GetModelAsync(object? user)
        => GetModelAsync((TUser)user!);

    /// <summary>
    /// Gets a view to display for the page.
    /// </summary>
    public abstract Task<PageView> ViewAsync(TUser user);

    /// <summary>
    /// Gets a view to display for the page.
    /// </summary>
    public virtual async Task<object?> GetModelAsync(TUser user)
        => null;


    /// <summary>
    /// Gets a view indicating the page is forbidden.
    /// </summary>
    protected PageView ForbiddenView()
        => PageView.Forbidden(this);

    /// <summary>
    /// Gets a view indicating the page is read-only, with the given title and summary items.
    /// </summary>
    protected PageView SummaryOnlyView(string title, params IEnumerable<PageSummaryItem> items)
        => PageView.SummaryOnly(this, title, [.. items]);

    /// <summary>
    /// Gets a view indicating the page can be interacted with through a link but should not otherwise be shown as editable, with the given title and summary items.
    /// </summary>
    protected PageView EditableViaLinkView(string title, params IEnumerable<PageSummaryItem> items)
        => PageView.Editable(this, title, null, [.. items]);

    /// <summary>
    /// Gets a view indicating the page can be interacted with, with the given title, action verb indicating interaction, and summary items.
    /// </summary>
    protected PageView EditableView(string title, string action, params IEnumerable<PageSummaryItem> items)
        => PageView.Editable(this, title, action, [.. items]);

    /// <summary>
    /// Gets a view indicating the page must be interacted with before the user can proceed, using the given title.
    /// </summary>
    protected PageView RequiredView(string title)
        => PageView.Required(this, title);


    /// <summary>
    /// Creates a status message indicating no changes happened.
    /// For instance, if a user attempted to edit something setting the new value to the value that was already there.
    /// </summary>
    protected StatusMessage NoChange()
        => new(Status.None, "");

    /// <summary>
    /// Creates a status message indicating an operation was successful, using the given textual description.
    /// </summary>
    protected StatusMessage Success(string text)
        => new(Status.Success, text);

    /// <summary>
    /// Creates a status message indicating an operation successfully returned important information the user must know about, using the given textual description.
    /// </summary>
    protected StatusMessage ImportantInformation(string text)
        => new(Status.ImportantInformation, text);

    /// <summary>
    /// Creates a status message indicating the user made an error, using the given textual description.
    /// </summary>
    protected StatusMessage Error(string text)
        => new(Status.UserError, text);
}