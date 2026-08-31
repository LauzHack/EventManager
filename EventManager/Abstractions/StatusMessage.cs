namespace EventManager.Abstractions;

/// <summary>
/// Message including both a status and text, returned as a result of an action.
/// </summary>
public sealed record StatusMessage(Status Status, string Text);