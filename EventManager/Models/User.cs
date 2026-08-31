namespace EventManager.Models;

/// <summary>
/// The basic properties of a user.
/// </summary>
public abstract class User
{
    /// <summary>
    /// Unique identifier for the user.
    /// This identifier is only unique among a type of users, not across the entire database.
    /// </summary>
    public abstract string Id { get; }
}