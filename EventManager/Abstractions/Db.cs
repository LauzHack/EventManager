using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using EventManager.Models;

namespace EventManager.Abstractions;

/// <summary>
/// Database storing instances of models.
/// </summary>
public abstract class Db : IAsyncDisposable
{
    public abstract DbValues<Admin> Admins { get; }

    public abstract DbValues<ApplicationGroup> ApplicationGroups { get; }

    public abstract DbValues<AuditMessage> AuditMessages { get; }

    public abstract DbValues<Award> Awards { get; }

    public abstract DbValues<ChallengeSetter> ChallengeSetters { get; }

    public abstract DbValues<StoredConfigValue> ConfigValues { get; }

    public abstract DbValues<Currency> Currencies { get; }

    public abstract DbValues<Letter> Letters { get; }

    public abstract DbValues<Participant> Participants { get; }

    public abstract DbValues<Project> Projects { get; }

    public abstract DbValues<TravelExpense> TravelExpenses { get; }

    /// <summary>
    /// Event triggered when the config values are imported via <see cref="OverwriteAsync" />.
    /// </summary>
    public event EventHandler<IReadOnlyCollection<StoredConfigValue>>? ConfigValuesOverwritten;

    /// <summary>
    /// Initializes the database.
    /// Can be called multiple times without further effect.
    /// </summary>
    public abstract Task InitializeAsync();

    /// <summary>
    /// Commits all changes to the database.
    /// It is acceptable to call this on a disposed instance, since no changes can have been made to it.
    /// </summary>
    /// <returns>True if any changes were made.</returns>
    public abstract Task<bool> CommitAsync();

    /// <summary>
    /// Throws an exception if there are any changes.
    /// It is acceptable to call this on a disposed instance, since no changes can have been made to it.
    /// </summary>
    public abstract void EnsureNoChanges();

    /// <summary>
    /// Cancels all changes.
    /// It is acceptable to call this on a disposed instance, since no changes can have been made to it.
    /// </summary>
    public abstract void CancelChanges();

    /// <summary>
    /// Exports this database as a stream and disposes this instance.
    /// Creating further instances of this database will invalidate the stream.
    /// </summary>
    public abstract Task<Stream> ExportAndDisposeAsync();

    /// <summary>
    /// Overwrites this database's contents with the given stream.
    /// The contents must be equivalent to what was returned from a call to <see cref="ExportAndDisposeAsync" />.
    /// The stream will not be disposed.
    /// </summary>
    public abstract Task OverwriteAsync(Stream stream);

    /// <summary>
    /// Disposes of this database instance.
    /// Fails if any changes are pending.
    /// </summary>
    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Gets the database values represented by the given type, or null if the type does not match database values.
    /// </summary>
    public object? GetValues(Type type)
    {
        foreach (var prop in typeof(Db).GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType.IsAssignableTo(type))
            {
                return prop.GetValue(this);
            }
        }
        return null;
    }

    /// <summary>
    /// Triggers the <see cref="ConfigValuesOverwritten" /> event using the given values.
    /// </summary>
    protected void TriggerConfigValuesOverwritten(IReadOnlyCollection<StoredConfigValue> values)
        => ConfigValuesOverwritten?.Invoke(this, values);
}