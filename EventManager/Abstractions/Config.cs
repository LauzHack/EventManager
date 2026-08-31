using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

using EventManager.Models;

namespace EventManager.Abstractions;

// This file uses nullable suppressions `!` for `Type.FullName`,
// I think Type.FullName being null can only happen if you inherit from Type yourself...

/// <summary>
/// Provides access to configuration data from the database.
/// </summary>
/// <remarks>
/// Only one instance of this type should be in use at any given time, as it internally caches values.
/// </remarks>
public sealed class Config
{
    private readonly Db _database;
    private Dictionary<string, CacheableValue> _values;

    public AuthenticationSecret? AuthenticationSecret
        => Get<AuthenticationSecret>();

    public EmailSenderSettings? EmailSenderSettings
        => Get<EmailSenderSettings>();

    public EventDetails? EventDetails
        => Get<EventDetails>();

    public EventHints EventHints
        => Get(() => EventHints.Default);

    public EventLimits? EventLimits
        => Get<EventLimits>();

    public EventStatus EventStatus
        => Get<EventStatus>();

    public EventTheme? EventTheme
        => Get<EventTheme>();

    public LetterData? LetterData
        => Get<LetterData>();

    public ProfileForm? ProfileForm
        => Get<ProfileForm>();

    public TravelReimbursementPolicy? TravelReimbursementPolicy
        => Get<TravelReimbursementPolicy>();

    public VisaInvitationFormat? VisaInvitationFormat
        => Get<VisaInvitationFormat>();

    /// <summary>
    /// Creates a configuration provider using the given database.
    /// </summary>
    public static async Task<Config> CreateAsync(Db database)
    {
        var stored = await database.ConfigValues.ToCollectionAsync();
        return new Config(database, stored);
    }

    private Config(Db database, IReadOnlyCollection<StoredConfigValue> values)
    {
        _database = database;
        _values = ToCacheableValues(values);
        _database.ConfigValuesOverwritten += (_, vals) => _values = ToCacheableValues(vals);
    }

    /// <summary>
    /// Gets the configuration value of the given type, or returns the default for that type if there is no such value.
    /// </summary>
    [return: NotNullIfNotNull(nameof(factory))]
    public T? Get<T>(Func<T>? factory = null)
        where T : notnull
    {
        if (_values.TryGetValue(typeof(T).FullName!, out var value))
        {
            if (value.Cached is T cachedResult)
            {
                return cachedResult;
            }

            var deserialized = JsonSerializer.Deserialize<T>(value.Stored.Value);
            _values[typeof(T).FullName!] = new(value.Stored, deserialized);
            return deserialized;
        }

        if (factory is not null)
        {
            // We do *not* set the stored value here, since this may be called in a read-only system request.
            return factory();
        }

        return default;
    }

    /// <summary>
    /// Attempts to get the configuration value of the given type, returning true if and only if it was found.
    /// </summary>
    public bool TryGet(Type type, out object? result)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ConfigValue<>))
        {
            result = type.GetConstructor([typeof(Config)])!.Invoke([this]);
            return true;
        }

        foreach (var prop in typeof(Config).GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType.IsAssignableTo(type) && prop.GetMethod is not null)
            {
                result = prop.GetMethod.Invoke(this, []);
                return true;
            }
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Sets a configuration value.
    /// </summary>
    public void Set(object value)
    {
        var type = value.GetType();
        if (_values.TryGetValue(type.FullName!, out var existing))
        {
            _database.ConfigValues.Remove(existing.Stored);
        }

        var serialized = JsonSerializer.Serialize(value);
        var newCacheable = new CacheableValue(new StoredConfigValue(type.FullName!, serialized), value);
        _values[type.FullName!] = newCacheable;
        _database.ConfigValues.Add(newCacheable.Stored);
    }

    private static Dictionary<string, CacheableValue> ToCacheableValues(IReadOnlyCollection<StoredConfigValue> values)
        => values.ToDictionary(v => v.TypeName, v => new CacheableValue(v, null), StringComparer.Ordinal);

    private sealed record CacheableValue(StoredConfigValue Stored, object? Cached);
}