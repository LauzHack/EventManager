using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using EventManager.Models;

namespace EventManager.Abstractions;

/// <summary>
/// Result of executing an operation.
/// </summary>
/// <param name="Status">The result status.</param>
/// <param name="Message">The result message, which can be empty if the status is "none".</param>
public abstract record OperationResult(Status Status, string Message)
{
    /// <summary>
    /// Display of a page.
    /// </summary>
    /// <param name="User">The user associated with the operation, if any.</param>
    /// <param name="View">The page view.</param>
    /// <param name="Model">The page model.</param>
    /// <param name="AvailableViews">The page views available to the user.</param>
    /// <param name="Configuration">The system configuration, which may be useful when displaying the result.</param>
    public sealed record Page(User? User, Status Status, string Message, PageView View, object? Model, ImmutableArray<PageView> AvailableViews, Config Configuration)
        : OperationResult(Status, Message);

    /// <summary>
    /// Display of a letter.
    /// </summary>
    /// <param name="Body">The letter body.</param>
    /// <param name="DateTime">The date and time at which the letter was generated.</param>
    /// <param name="Data">The configured letter data.</param>
    public sealed record Letter(string Body, DateTimeOffset DateTime, LetterData Data)
        : OperationResult(Status.None, "");

    /// <summary>
    /// Display of a file.
    /// </summary>
    /// <param name="RequestedFile">The file.</param>
    public sealed record File(Abstractions.File RequestedFile)
        : OperationResult(Status.None, "");

    /// <summary>
    /// Display of the public challenges list.
    /// </summary>
    /// <param name="EventTitle">The event title.</param>
    /// <param name="Setters">The challenge setters, in order.</param>
    public sealed record Challenges(string EventTitle, IReadOnlyCollection<ChallengeSetter> Setters)
        : OperationResult(Status.None, "");

    /// <summary>
    /// Display of the public projects gallery.
    /// </summary>
    /// <param name="EventTitle">The event title.</param>
    /// <param name="Contents">The projects, in order.</param>
    /// <param name="Awards">The awards, if public.</param>
    public sealed record Projects(string EventTitle, IReadOnlyCollection<Project> Contents, IReadOnlyDictionary<Project, IReadOnlyCollection<string>> Awards)
        : OperationResult(Status.None, "");

    /// <summary>
    /// Successful execution of an action.
    /// </summary>
    public sealed record Action(Status Status, string Message, PageView View)
        : OperationResult(Status, Message);

    /// <summary>
    /// Authentication error.
    /// </summary>
    public sealed record AuthenticationRequired()
        : OperationResult(Status.UserError, "Unauthenticated attempt to access a page that requires authentication.");

    /// <summary>
    /// The user attempted to interact with a page that does not allow interaction in the current status of the user and system.
    /// </summary>
    public sealed record Unavailable()
        : OperationResult(Status.UserError, "Attempt to access an unavailable page.");

    /// <summary>
    /// The user attempted to interact with the system in an unknown manner.
    /// </summary>
    public sealed record NotFound()
        : OperationResult(Status.UserError, "Attempt to access a nonexistent page.");

    /// <summary>
    /// Something went wrong, and the user can do something about it.
    /// </summary>
    public sealed record UserError(string Message)
        : OperationResult(Status.UserError, "Bad request: " + Message);

    /// <summary>
    /// Something went wrong, but it's not the user's fault.
    /// </summary>
    public sealed record SystemError(string Message)
        : OperationResult(Status.SystemError, Message)
    {
        public SystemError(Exception e) : this($"{e.Message}{Environment.NewLine}```{Environment.NewLine}{e.StackTrace}{Environment.NewLine}```") { }
    }
}