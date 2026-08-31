using EventManager.Models;

namespace EventManager.Abstractions;

/// <summary>
/// Email, with an optional link to an operation and an optional event invitation.
/// </summary>
/// <param name="Recipient">The email address of the recipient.</param>
/// <param name="Subject">The email subject.</param>
/// <param name="Body">The email body, which can contain Markdown.</param>
/// <param name="Operation">The operation the email should link to, if any.</param>
/// <param name="OperationDescription">The text to use when linking to the operation, if not the default.</param>
/// <param name="AttachedEvent">The event attached to the email, if any.<</param>
public sealed record Email(
    string Recipient,
    string Subject,
    string Body,
    Operation? Operation,
    string? OperationDescription = null,
    EventDetails? AttachedEvent = null
);