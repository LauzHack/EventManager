using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;

namespace EventManager.Tests.TestInfrastructure;

public sealed class FakeEmailSender(bool enabled = true) : EmailSender
{
    public List<Email> Outbox { get; } = [];
    public EmailSenderSettings? LastSettings { get; private set; }
    public AuthenticationSecret? LastSecret { get; private set; }

    public override async Task SendAsync(IReadOnlyCollection<Email> emails, EmailSenderSettings? overrideSettings = null, AuthenticationSecret? overrideSecret = null)
    {
        if (!enabled && emails.Count > 0)
        {
            throw new InvalidOperationException("Disabled!");
        }
        Outbox.AddRange(emails);
        LastSettings = overrideSettings;
        LastSecret = overrideSecret;
    }

    public override async Task SendCopyAsync(string subject, string body, IReadOnlyCollection<string> recipients, Operation? operation = null, string? operationDescription = null)
    {
        Outbox.AddRange(recipients.Select(r => new Email(r, subject, body, operation, operationDescription)));
    }
}