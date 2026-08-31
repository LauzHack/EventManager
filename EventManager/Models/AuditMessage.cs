using System;

using EventManager.Abstractions;

namespace EventManager.Models;

/// <summary>
/// An immutable message representing an action on the system, stored for audit purposes.
/// </summary>
/// <param name="Status">The status of the message, indicating how important it is.</param>
/// <param name="Text">The text of the message.</param>
/// <param name="EmailAddress">The email address of the user related to the message, if any.</param>
/// <param name="Source">The name of the message source.</param>
/// <param name="DateTime">The date and time of the message.</param>
public sealed record AuditMessage(Status Status, string Text, string? EmailAddress, string Source, DateTimeOffset DateTime);