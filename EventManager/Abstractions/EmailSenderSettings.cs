using System;

namespace EventManager.Abstractions;

/// <summary>
/// Settings used by an email sender.
/// </summary>
/// <param name="Uri">The URI of the server.</param>
/// <param name="UserName">The user name to log in to the server.</param>
/// <param name="Password">The password to log in to the server.</param>
/// <param name="SenderName">The name to use when sending emails.</param>
/// <param name="SenderAddress">The email address to use when sending emails.</param>
/// <param name="ReplyToAddress">The email address recipients should reply to.</param>
public sealed record EmailSenderSettings(
    Uri Uri,
    string UserName,
    string Password,
    string SenderName,
    string SenderAddress,
    string ReplyToAddress
);