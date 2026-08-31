using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

using MailKit.Net.Smtp;

using MimeKit;
using MimeKit.Text;

namespace EventManager.Web;

public sealed class MailKitEmailSender(Uri baseUri, EventDetails? details, EventTheme? theme, EmailSenderSettings? configSettings, AuthenticationSecret? authSecret) : EmailSender
{
    public override async Task SendAsync(IReadOnlyCollection<Email> emails, EmailSenderSettings? overrideSettings = null, AuthenticationSecret? overrideSecret = null)
    {
        var settings = overrideSettings ?? configSettings ?? throw new InvalidOperationException("There should be either settings from config or provided as override");
        var secret = overrideSecret ?? authSecret ?? throw new InvalidOperationException("There should be an auth secret from config or provided as override");

        if (emails.Count == 0)
        {
            // No point in connecting to do nothing
            return;
        }

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Uri);
        await client.AuthenticateAsync(settings.UserName, settings.Password);

        foreach (var email in emails)
        {
            var subject = email.Subject;
            if (details is not null)
            {
                subject = $"[{details.Title}] {subject}";
            }

            var body = Markdown.ToHtml(email.Body);
            // a bit hacky, but good enough: instead of "strong", use highlighted bold text to really catch people's attention
            body = body.Replace("<strong>", "<mark style=\"font-weight: bold;\">", StringComparison.Ordinal)
                       .Replace("</strong>", "</mark>", StringComparison.Ordinal);

            if (email.Operation is Operation op)
            {
                op = Authenticator.AddAuthentication(secret, op, email.Recipient);
                body += OperationHtml(op, email.OperationDescription);
            }

            MimeEntity mimeBody = new TextPart(TextFormat.Html) { Text = body };
            if (email.AttachedEvent is not null)
            {
                mimeBody = new Multipart("mixed")
                {
                    mimeBody,
                    new TextPart("text/calendar") { FileName = "event.ics", Text = email.AttachedEvent.ToIcsText(DateTimeOffset.Now, Guid.NewGuid()) }
                };
            }

            using var message = new MimeMessage
            {
                From = { new MailboxAddress(settings.SenderName, settings.SenderAddress) },
                ReplyTo = { new MailboxAddress(settings.SenderName, settings.ReplyToAddress) },
                To = { new MailboxAddress(email.Recipient, email.Recipient) },
                Subject = subject,
                Body = mimeBody
            };

            await client.SendAsync(message);
        }

        await client.DisconnectAsync(quit: true);
    }

    public override async Task SendCopyAsync(string subject, string body, IReadOnlyCollection<string> recipients, Operation? operation = null, string? operationDescription = null)
    {
        if (configSettings is null)
        {
            throw new InvalidOperationException($"Cannot use {nameof(SendCopyAsync)} until settings have been configured.");
        }

        if (recipients.Count == 0)
        {
            // Some SMTP servers do not like receiving 0 recipients for an email
            return;
        }

        var htmlBody = Markdown.ToHtml(body);
        if (operation is not null)
        {
            htmlBody += OperationHtml(operation, operationDescription);
        }
        using var message = new MimeMessage
        {
            From = { new MailboxAddress(configSettings.SenderName, configSettings.SenderAddress) },
            ReplyTo = { new MailboxAddress(configSettings.SenderName, configSettings.ReplyToAddress) },
            To = { new GroupAddress("undisclosed-recipients") },
            Subject = subject,
            Body = new TextPart(TextFormat.Html) { Text = htmlBody }
        };
        foreach (var emailAddress in recipients)
        {
            message.Bcc.Add(new MailboxAddress(emailAddress, emailAddress));
        }

        using var client = new SmtpClient();
        await client.ConnectAsync(configSettings.Uri);
        await client.AuthenticateAsync(configSettings.UserName, configSettings.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(quit: true);
    }

    private string OperationHtml(Operation operation, string? operationDescription)
    {
        var result = "<br>";
        result += $"<a href=\"{new Uri(baseUri, operation.RelativeUri)}\" ";
        // button with slightly rounded corners
        string backCol = theme?.BackgroundColor?.ToString() ?? "#ddd";
        string foreCol = theme?.ForegroundColor?.ToString() ?? "#000";
        result += $"style=\"padding: 0.5em 1em; border-radius: 0.3em; background-color: {backCol}; color: {foreCol}; font-weight: bold; text-decoration: none; display: block; text-align: center; max-width: 20rem\"";
        result += $">{operationDescription ?? DefaultOperationText}</a>";
        return result;
    }
}