using System.Collections.Immutable;

namespace EventManager.Abstractions;

/// <summary>
/// Server-side secret for authentication. Must not leak, ever, and must not change within the lifetime of an instance of this system.
/// </summary>
/// <param name="HashKey">Key used to hash email addresses for login.</param>
public sealed record AuthenticationSecret(ImmutableArray<byte> HashKey);