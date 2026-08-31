using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using EventManager.Models;

namespace EventManager.Abstractions;

/// <summary>
/// An operation performed by an end user on the system.
/// </summary>
/// <param name="UserType">The type of user for this operation.</param>
/// <param name="Arguments">The operation arguments.</param>
public abstract record Operation(Type UserType, OperationArguments Arguments)
{
    /// <summary>
    /// The default user type in the system.
    /// </summary>
    protected static readonly Type DefaultUserType = typeof(Participant);

    /// <summary>
    /// Whether this operation only reads data from the system.
    /// </summary>
    public abstract bool IsReadOnly { get; }

    /// <summary>
    /// Whether this is the default operation of the system.
    /// </summary>
    public virtual bool IsDefault
        => false;

    /// <summary>
    /// The relative URI that uniquely identifies this operation and its arguments.
    /// </summary>
    public Uri RelativeUri
    {
        get
        {
            var uri = new Uri("/" + string.Join('/', [.. PathSegments.Select(Uri.EscapeDataString)]), UriKind.Relative);
            if (Arguments is not null)
            {
                uri = Arguments.AppendTextValuesToUri(uri);
            }
            return uri;
        }
    }

    /// <summary>
    /// Path segments to be used in the relative URI.
    /// </summary>
    protected abstract string[] PathSegments { get; }

    /// <summary>
    /// Human-friendly version of the relative URI, without arguments.
    /// </summary>
    public sealed override string ToString()
    {
        return "/" + string.Join('/', PathSegments);
    }

    /// <summary>
    /// Parses an operation from the given URI.
    /// </summary>
    public static Operation Parse(Uri uri)
    {
        var segments = uri.Segments.Select(s => Uri.UnescapeDataString(s.TrimEnd('/'))).Where(s => s.Length > 0).ToArray();
        var args = OperationArguments.FromUri(uri);

        if (segments.Length == 0)
        {
            return new DefaultOperation(args);
        }

        if (segments.Length == 2 && segments[0].Equals("Letter", StringComparison.OrdinalIgnoreCase))
        {
            return new LetterViewOperation(segments[1]);
        }
        if (segments.Length == 2 && segments[0].Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            return new FileViewOperation(segments[1]);
        }
        if (segments.Length == 1 && segments[0].Equals("Challenges", StringComparison.OrdinalIgnoreCase))
        {
            return new ChallengesViewOperation();
        }
        if (segments.Length == 1 && segments[0].Equals("Projects", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectsViewOperation();
        }

        var userType = NameToUserType(segments[0]);
        if (userType is not null)
        {
            if (segments.Length == 1)
            {
                return new DefaultPageViewOperation(userType, args);
            }
            if (segments is [_, var page])
            {
                return new PageViewOperation(userType, page, args);
            }
            if (segments is [_, var page2, var method])
            {
                return new PageActionOperation(userType, page2, method, args);
            }
        }

        return new UnknownOperation(segments);
    }

    /// <summary>
    /// Creates a page view operation for the given user type.
    /// </summary>
    public static Operation CreatePageView<TUser>()
        where TUser : User
        => new DefaultPageViewOperation(typeof(TUser), OperationArguments.Empty);

    /// <summary>
    /// Creates a page view operation for the given user type.
    /// </summary>
    public static Operation CreatePageView(Type userType)
        => new DefaultPageViewOperation(userType, OperationArguments.Empty);

    /// <summary>
    /// Creates a page view operation for the given page.
    /// </summary>
    public static Operation CreatePageView(Page page)
        => new PageViewOperation(page.UserType, PageTypeToName(page.GetType()), OperationArguments.Empty);

    /// <summary>
    /// Creates a page view operation for the given page and user type.
    /// </summary>
    public static Operation CreatePageView<TUser, TPage>()
        where TUser : User?
        where TPage : Page<TUser>
        => new PageViewOperation(typeof(TUser), PageTypeToName(typeof(TPage)), OperationArguments.Empty);

    /// <summary>
    /// Creates a page action operation for the given page and user types, the given method name, and with the given arguments.
    /// </summary>
    public static Operation CreatePageAction<TUser, TPage>(string fullMethodName, params (string, string)[] arguments)
        where TUser : User?
        where TPage : Page<TUser>
    {
        var method = typeof(TPage).GetMethod(fullMethodName)
                  ?? throw new ArgumentException("Unknown method.", nameof(fullMethodName));
        return new PageActionOperation(
            typeof(TUser),
            PageTypeToName(typeof(TPage)),
            MethodToName(method),
            OperationArguments.FromPairs(arguments)
        );
    }

    /// <summary>
    /// Creates a letter view operation for the given letter ID.
    /// </summary>
    public static Operation CreateLetterView(string id)
        => new LetterViewOperation(id);

    /// <summary>
    /// Creates a file view operation for the given file ID.
    /// </summary>
    public static Operation CreateFileView(string id)
        => new FileViewOperation(id);

    /// <summary>
    /// Executes this operation using the given user, system pages, and system dependencies.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "At module boundary, we need to catch all possible failures here")]
    public async Task<OperationResult> ExecuteAsync(User? user, SystemPages pages, SystemDependencies dependencies)
    {
        try
        {
            return await ExecuteCoreAsync(user, pages, dependencies);
        }
        catch (AuthenticationRequiredException)
        {
            return new OperationResult.AuthenticationRequired();
        }
        catch (Exception e)
        {
            return new OperationResult.SystemError(e);
        }
    }

    /// <summary>
    /// Creates a new Operation equal to this one but with the given key-value pair added as textual argument.
    /// </summary>
    public Operation WithExtraTextArgument(string key, string value)
        => this with { Arguments = Arguments.WithText(key, value) };

    /// <summary>
    /// Core method for execution, wrapped in a try-catch that translates exceptions to operation results.
    /// </summary>
    protected abstract Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies);

    /// <summary>
    /// Gets the page with the given name, or the earliest required page if no name is provided, for the given user, using the given pages and dependencies.
    /// </summary>
    protected async Task<(PageView?, ImmutableArray<PageView>)> GetWithPreviousViewsAsync(User? user, SystemPages pages, SystemDependencies dependencies, string? pageName)
    {
        PageView? selectedView = null;
        var views = ImmutableArray.CreateBuilder<PageView>();
        foreach (var pageType in pages.GetPageTypes(UserType))
        {
            var page = await dependencies.CreatePageAsync(pageType);

            if (user is null && page.UserIsRequired)
            {
                throw new AuthenticationRequiredException();
            }

            var view = await page.ViewAsync(user);
            views.Add(view);

            if (PageTypeToName(pageType).Equals(pageName, StringComparison.Ordinal))
            {
                selectedView = view;
            }

            if (view.IsRequired)
            {
                if (pageName is null && selectedView is null)
                {
                    selectedView = view;
                }
                break;
            }
        }

        return (selectedView, views.ToImmutable());
    }

    /// <summary>
    /// Returns a "not found" or "forbidden" result depending on whether the given page name actually exists or not.
    /// </summary>
    protected OperationResult NotFoundOrForbidden(SystemPages pages, string pageName)
    {
        foreach (var pageType in pages.GetPageTypes(UserType))
        {
            if (PageTypeToName(pageType).Equals(pageName, StringComparison.Ordinal))
            {
                return new OperationResult.Unavailable();
            }
        }
        return new OperationResult.NotFound();
    }

    /// <summary>
    /// Translates the given name to a user type, or returns null if it does not match a user type.
    /// </summary>
    protected static Type? NameToUserType(string name)
    {
        var type = Type.GetType(typeof(User).Namespace + "." + name, throwOnError: false, ignoreCase: false);
        if (type is null || type.BaseType != typeof(User))
        {
            return null;
        }
        return type;
    }

    /// <summary>
    /// Translates the given page type to a name, to be used in URIs.
    /// </summary>
    protected static string PageTypeToName(Type type)
    {
        if (type.Name.EndsWith("Page", StringComparison.Ordinal))
        {
            return type.Name[..^"Page".Length];
        }
        return type.Name;
    }

    /// <summary>
    /// Translates the method to a name, to be used in URIs.
    /// </summary>
    protected static string MethodToName(MethodInfo method)
    {
        if (method.Name.EndsWith("Async", StringComparison.Ordinal))
        {
            return method.Name[..^"Async".Length];
        }
        return method.Name;
    }

    /// <summary>
    /// Adds the given arguments to the given operation.
    /// </summary>
    public static Operation operator +(Operation left, OperationArguments right)
        => left with { Arguments = left.Arguments + right };
}

/// <summary>
/// The default view operation of the system.
/// </summary>
file sealed record DefaultOperation(OperationArguments Arguments) : Operation(DefaultUserType, Arguments)
{
    public override bool IsReadOnly
        => true;

    public override bool IsDefault
        => true;

    protected override string[] PathSegments
        => [];

    protected override Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
        => new DefaultPageViewOperation(UserType, Arguments).ExecuteAsync(user, pages, dependencies);
}

/// <summary>
/// A view operation for the default page of the given user type.
/// </summary>
file sealed record DefaultPageViewOperation(Type UserType, OperationArguments Arguments) : Operation(UserType, Arguments)
{
    public override bool IsReadOnly
        => true;

    protected override string[] PathSegments
        => [UserType.Name];

    protected override async Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
    {
        var (current, previous) = await GetWithPreviousViewsAsync(user, pages, dependencies, null);
        if (current is null)
        {
            return new OperationResult.SystemError("No required page");
        }
        var model = await current.Page.GetModelAsync(user);
        return new OperationResult.Page(user, Status.None, "", current, model, previous, dependencies.Configuration);
    }
}

/// <summary>
/// A view operation on a page.
/// </summary>
file sealed record PageViewOperation(Type UserType, string PageName, OperationArguments Arguments) : Operation(UserType, Arguments)
{
    public override bool IsReadOnly
        => true;

    protected override string[] PathSegments
        => [UserType.Name, PageName];

    protected override async Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
    {
        var (current, previous) = await GetWithPreviousViewsAsync(user, pages, dependencies, PageName);
        if (current is null)
        {
            return NotFoundOrForbidden(pages, PageName);
        }
        if (!current.IsInteractable)
        {
            return new OperationResult.Unavailable();
        }
        var model = await current.Page.GetModelAsync(user);
        return new OperationResult.Page(user, Status.None, "", current, model, previous, dependencies.Configuration);
    }
}

/// <summary>
/// An action operation by a participant on a specific page.
/// </summary>
file sealed record PageActionOperation(Type UserType, string PageName, string MethodName, OperationArguments Arguments) : Operation(UserType, Arguments)
{
    public override bool IsReadOnly
        => false;

    protected override string[] PathSegments
        => [UserType.Name, PageName, MethodName];

    protected override async Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
    {
        // Empty files are always disallowed, and oversized files are only allowed for admins so that restoring a backup works regardless of the backup size
        if (Arguments.HasEmptyFiles || (Arguments.HasOversizedFiles && UserType != typeof(Admin)))
        {
            return new OperationResult.UserError("Attempt to upload empty or oversized files");
        }

        var (current, _) = await GetWithPreviousViewsAsync(user, pages, dependencies, PageName);
        if (current is null)
        {
            return NotFoundOrForbidden(pages, PageName);
        }
        if (!current.IsInteractable)
        {
            return new OperationResult.Unavailable();
        }

        var method = current.Page.GetType().GetMethods().FirstOrDefault(m => MethodName.Equals(MethodToName(m), StringComparison.Ordinal));
        if (method is null)
        {
            return new OperationResult.NotFound();
        }

        try
        {
            if (method.ReturnType == typeof(Task<File>))
            {
                var fileResult = await Arguments.Invoke<Task<File>>(current.Page, method, user);
                return new OperationResult.File(fileResult);
            }

            var result = await Arguments.Invoke<Task<StatusMessage>>(current.Page, method, user);
            return new OperationResult.Action(result.Status, result.Text, current);
        }
        catch (InvalidOperationException e)
        {
            return new OperationResult.UserError(e.Message);
        }
    }
}

/// <summary>
/// A view operation for a letter.
/// </summary>
file sealed record LetterViewOperation(string Id) : Operation(DefaultUserType, OperationArguments.Empty)
{
    public override bool IsReadOnly
        => true;

    protected override string[] PathSegments
        => ["Letter", Id];

    protected override async Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
    {
        if (dependencies.Configuration.LetterData is null)
        {
            return new OperationResult.NotFound();
        }
        var letter = await dependencies.Database.Letters.FindAsync(Id);
        if (letter is null)
        {
            return new OperationResult.NotFound();
        }
        return new OperationResult.Letter(letter.Body, letter.DateTime, dependencies.Configuration.LetterData);
    }
}

/// <summary>
/// A view operation for a file.
/// </summary>
file sealed record FileViewOperation(string Id) : Operation(DefaultUserType, OperationArguments.Empty)
{
    public override bool IsReadOnly
        => true;

    protected override string[] PathSegments
        => ["File", Id];

    protected override async Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
    {
        var file = await dependencies.FileStorage.GetFileAsync(Id);
        if (file is null)
        {
            return new OperationResult.NotFound();
        }
        return new OperationResult.File(file);
    }
}

/// <summary>
/// A view operation for the public challenges list.
/// </summary>
file sealed record ChallengesViewOperation() : Operation(DefaultUserType, OperationArguments.Empty)
{
    public override bool IsReadOnly
        => true;

    protected override string[] PathSegments
        => ["Challenges"];

    protected override async Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
    {
        if (dependencies.Configuration.EventDetails is null || dependencies.Configuration.EventStatus < EventStatus.CheckInStarted)
        {
            return new OperationResult.Unavailable();
        }
        var setters = await dependencies.Database.ChallengeSetters.OrderBy(s => s.Order).ToCollectionAsync();
        return new OperationResult.Challenges(dependencies.Configuration.EventDetails.Title, setters);
    }
}

/// <summary>
/// A view operation for the public projects gallery.
/// </summary>
file sealed record ProjectsViewOperation() : Operation(DefaultUserType, OperationArguments.Empty)
{
    public override bool IsReadOnly
        => true;

    protected override string[] PathSegments
        => ["Projects"];

    protected override async Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
    {
        if (dependencies.Configuration.EventDetails is null)
        {
            return new OperationResult.Unavailable();
        }

        var challengeSetters = await dependencies.Database.ChallengeSetters.OrderBy(s => s.Order).ToCollectionAsync();
        var projectsById = await dependencies.Database.Projects.ToDictionaryAsync(p => p.Id, StringComparer.Ordinal);
        var awards = new OrderedDictionary<Project, IReadOnlyCollection<string>>();
        if (dependencies.Configuration.EventStatus == EventStatus.Finished)
        {
            foreach (var setter in challengeSetters)
            {
                setter.BuildAwardsMapping(projectsById, awards);
            }
        }
        var nonAwardedProjects = projectsById.Values.Where(p => !awards.ContainsKey(p)).OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase);
        var orderedProjects = awards.Select(p => p.Key).Concat(nonAwardedProjects).ToArray();
        return new OperationResult.Projects(dependencies.Configuration.EventDetails.Title, orderedProjects, awards);
    }
}

/// <summary>
/// An unknown operation, obtained from an URI that could not be parsed.
/// </summary>
file sealed record UnknownOperation(string[] Segments) : Operation(DefaultUserType, OperationArguments.Empty)
{
    public override bool IsReadOnly
        => true;

    protected override string[] PathSegments
        => Segments;

    protected override async Task<OperationResult> ExecuteCoreAsync(User? user, SystemPages pages, SystemDependencies dependencies)
        => new OperationResult.NotFound();
}