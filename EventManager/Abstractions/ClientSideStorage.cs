using System.Diagnostics.CodeAnalysis;

namespace EventManager.Abstractions;

/// <summary>
/// Permanent storage on the client.
/// </summary>
/// <remarks>
/// Doesn't actually have to be 100% permanent, but users will be logged out if this storage expires.
/// </remarks>
public abstract class ClientSideStorage
{
    /// <summary>
    /// Attempts to get the value associated with the given key.
    /// </summary>
    public abstract bool TryGet(string key, [MaybeNullWhen(false)] out string value);

    /// <summary>
    /// Sets the value associated with the given key, overwriting it if needed.
    /// </summary>
    public abstract void Set(string key, string value);
}