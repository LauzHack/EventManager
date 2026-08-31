using System;
using System.Security.Cryptography;
using System.Text;

namespace EventManager.Abstractions;

/// <summary>
/// Helper class to deterministically create IDs that are "unique enough".
/// </summary>
public static class DeterministicId
{
    /// <summary>
    /// Creates an ID for the given name using a timestamp from the given provider.
    /// Two IDs created at a different timestamp will definitely be different,
    /// and two IDs created at the same timestamp for a different name will likely be different.
    /// The timestamp cannot easily be guessed from the ID.
    /// The ID is hexadecimal and can thus be used for, e.g., URL parameters without requiring encoding.
    /// </summary>
    public static string Create(string name, TimeProvider timeProvider)
    {
        // We don't care about byte order here because we don't need determinism across machines.
        Span<byte> bytes = stackalloc byte[64];
        long timestamp = timeProvider.GetTimestamp();
        SHA256.HashData(Encoding.UTF8.GetBytes(name), bytes[..32]);
        SHA256.HashData(BitConverter.GetBytes(timestamp), bytes[32..]);
        return Convert.ToHexString(bytes);
    }
}