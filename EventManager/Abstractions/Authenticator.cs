using System;
using System.Security.Cryptography;
using System.Text;

namespace EventManager.Abstractions;

/// <summary>
/// Authenticates users by hashing their ID with a secret, and checks such authentications.
/// </summary>
public static class Authenticator
{
    /// <summary>
    /// The size the hash key should ideally have.
    /// </summary>
    public const int DesiredKeySizeInBytes = 64;

    /// <summary>
    /// Adds authentication for the given ID to the given operation.
    /// </summary>
    public static Operation AddAuthentication(AuthenticationSecret secret, Operation operation, string userId)
    {
        return operation.WithExtraTextArgument(IdKey(operation.UserType), userId)
                        .WithExtraTextArgument(HashedIdKey(operation.UserType), Convert.ToBase64String(Hash(secret, userId, operation.UserType)));
    }

    /// <summary>
    /// Attempts to log a user in for the given operation using the given client-side storage.
    /// Returns the ID if successful, or null otherwise.
    /// </summary>
    public static string? LogUserIn(AuthenticationSecret secret, Operation operation, ClientSideStorage storage)
    {
        if (operation.Arguments.TryGetText(IdKey(operation.UserType), out var id)
         && operation.Arguments.TryGetText(HashedIdKey(operation.UserType), out var hashedId))
        {
            storage.Set(IdKey(operation.UserType), id);
            storage.Set(HashedIdKey(operation.UserType), hashedId);
        }

        if (storage.TryGet(IdKey(operation.UserType), out id)
         && storage.TryGet(HashedIdKey(operation.UserType), out hashedId)
         // This is probably useless given the time noise from DB queries and such, but just in case
         && CryptographicOperations.FixedTimeEquals(Hash(secret, id, operation.UserType), Convert.FromBase64String(hashedId)))
        {
            return id;
        }

        return null;
    }

    private static Span<byte> Hash(AuthenticationSecret secret, string id, Type userType)
        => HMACSHA256.HashData(secret.HashKey.AsSpan(), Encoding.UTF8.GetBytes(userType.Name + '\0' + id));

    private static string IdKey(Type userType)
        => $"Auth.{userType.Name}.Id";

    private static string HashedIdKey(Type userType)
        => $"Auth.{userType.Name}.Hash";
}