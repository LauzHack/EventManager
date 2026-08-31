using System.Collections.Generic;
using System.Threading.Tasks;

using EventManager.Models;

namespace EventManager.Abstractions;

/// <summary>
/// Email sender.
/// </summary>
public abstract class EmailSender
{
    public const string DefaultOperationText = "View";

    /// <summary>
    /// Sends the given emails.
    /// </summary>
    /// <param name="emails">The emails to send, which may be to different recipients.</param>
    /// <param name="overrideSettings">The settings to use, overriding the ones fetched from configuration.</param>
    /// <param name="overrideSecret">The authentication secret to use for operation links, overriding the one fetched from configuration.</param>
    public abstract Task SendAsync(IReadOnlyCollection<Email> emails, EmailSenderSettings? overrideSettings = null, AuthenticationSecret? overrideSecret = null);

    /// <summary>
    /// Sends a simple non-customizeable email to many people, which can be more efficient than sending many individual emails.
    /// </summary>
    /// <param name="subject">The email subject.</param>
    /// <param name="body">The email body, which can contain Markdown.</param>
    /// <param name="recipients">The email addresses of the recipients.</param>
    /// <param name="operation">The operation the email should link to if any, which cannot require a user as it will not be personalized.</param>
    /// <param name="operationDescription">The text to use when linking to the operation, if not the default.</param>
    public abstract Task SendCopyAsync(string subject, string body, IReadOnlyCollection<string> recipients, Operation? operation = null, string? operationDescription = null);

    /// <summary>
    /// Sends the given email.
    /// </summary>
    /// <param name="recipient">The email address of the recipient.</param>
    /// <param name="subject">The email subject.</param>
    /// <param name="body">The email body, which can contain Markdown.</param>
    /// <param name="operation">The operation the email should link to, if any.</param>
    /// <param name="operationDescription">The text to use when linking to the operation, if not default.</param>
    /// <param name="attachedEvent">The event attached to the email, if any.</param>
    public Task SendEmailAsync(
        string recipient,
        string subject,
        string body,
        Operation? operation,
        string? operationDescription = null,
        EventDetails? attachedEvent = null
    ) => SendAsync([new Email(recipient, subject, body, operation, operationDescription, attachedEvent)]);
}