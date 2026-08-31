using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

/// <summary>
/// Contains all dependencies of the system.
/// </summary>
public sealed class SystemDependencies
{
    private static readonly NullabilityInfoContext _nullabilityInfoContext = new();
    // very basic chain detection with obvious limitations, good enough for now
    private readonly HashSet<Type> _currentChain;

    /// <summary>
    /// The system configuration, wrapping the database.
    /// </summary>
    public Config Configuration { get; }

    /// <summary>
    /// The system database.
    /// </summary>
    public Db Database { get; }

    /// <summary>
    /// The system file storage.
    /// </summary>
    public FileStorage FileStorage { get; }

    /// <summary>
    /// The system email sender.
    /// </summary>
    public EmailSender EmailSender { get; }

    /// <summary>
    /// The system time provider.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Creates system dependencies using the given database, file storage, and a way to create the email sender from a configuration.
    /// </summary>
    public static async Task<SystemDependencies> CreateAsync(Db database, FileStorage fileStorage, Func<Config, EmailSender> emailSenderCreator, TimeProvider timeProvider)
    {
        await database.InitializeAsync();
        var config = await Config.CreateAsync(database);
        return new(config, database, fileStorage, emailSenderCreator(config), timeProvider);
    }

    private SystemDependencies(Config configuration, Db database, FileStorage fileStorage, EmailSender emailSender, TimeProvider timeProvider)
    {
        Configuration = configuration;
        Database = database;
        FileStorage = fileStorage;
        EmailSender = emailSender;
        TimeProvider = timeProvider;
        _currentChain = [];
    }

    /// <summary>
    /// Creates a page of the given type, resolving constructor dependencies as necessary.
    /// </summary>
    public Task<Page> CreatePageAsync(Type type)
        => ResolveAsync<Page>(type);

    /// <summary>
    /// Creates a periodic task of the given type, resolving constructor dependencies as necessary.
    /// </summary>
    public Task<PeriodicTask> CreatePeriodicTaskAsync(Type type)
        => ResolveAsync<PeriodicTask>(type);


    private async Task<T> ResolveAsync<T>(Type exactType)
    {
        var result = await ResolveAsync(exactType, required: true);
        _currentChain.Clear();
        return (T)result!;
    }

    private async Task<object?> ResolveAsync(Type type, [NotNullWhen(true)] bool required)
    {
        if (!_currentChain.Add(type))
        {
            throw new InvalidOperationException($"Cycle detected: {string.Join(", ", type)}");
        }

        if (type == typeof(Db))
        {
            return Database;
        }
        if (type == typeof(FileStorage))
        {
            return FileStorage;
        }
        if (type == typeof(EmailSender))
        {
            return EmailSender;
        }
        if (type == typeof(TimeProvider))
        {
            return TimeProvider;
        }

        if (Configuration.TryGet(type, out object? configValue))
        {
            if (required && configValue is null)
            {
                throw new ArgumentException($"Type {type.FullName} is required and should be in configuration but is missing", nameof(type));
            }
            return configValue;
        }

        if (Database.GetValues(type) is object dbValues)
        {
            return dbValues;
        }

        if (type.IsAbstract)
        {
            if (required)
            {
                throw new ArgumentException($"Type {type.FullName} is required but is an abstract type", nameof(type));
            }
            return null;
        }

        var ctors = type.GetConstructors();
        if (ctors is not [var loneCtor])
        {
            throw new ArgumentException($"Expected exactly one ctor for {type.Name}", nameof(type));
        }

        var args = await Task.WhenAll(
            loneCtor.GetParameters()
                    .Select(p => ResolveAsync(p.ParameterType, required: _nullabilityInfoContext.Create(p).WriteState == NullabilityState.NotNull))
        );
        return loneCtor.Invoke(args);
    }
}