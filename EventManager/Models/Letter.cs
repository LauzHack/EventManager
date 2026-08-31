using System;

namespace EventManager.Models;

/// <summary>
/// An immutable letter that is publicly available as a static page on the system.
/// </summary>
/// <param name="Id">The letter's ID.</param>
/// <param name="Body">The body of the letter, as Markdown text.</param>
/// <param name="DateTime">The date and time at which the letter was created.</param>
public sealed record Letter(string Id, string Body, DateTimeOffset DateTime);