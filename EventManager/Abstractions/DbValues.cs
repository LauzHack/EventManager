using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

/// <summary>
/// Provides read and write access to values stored in the database.
/// Changes are only persisted once the commit operation on the database finishes.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <remarks>
/// Note for implementers: all IQueryables returned by this class MUST also be IAsyncEnumerables.
/// This cannot be the case statically because otherwise calls to extension methods such as `.Where` become ambiguous.
/// (Entity Framework Core guarantees this, see https://github.com/dotnet/efcore/pull/24145#discussion_r575826025)
/// ---
/// There is no "read only" variant of this class because its contents would either be mutable anyway,
/// or look mutable but actually not be stored, both of which would be pits of failure.
/// </remarks>
public abstract class DbValues<T> : IQueryable<T>
{
    /// <summary>
    /// See IQueryable.
    /// </summary>
    public Type ElementType
        => typeof(T);

    /// <summary>
    /// See IQueryable.
    /// </summary>
    public abstract Expression Expression { get; }

    /// <summary>
    /// See IQueryable.
    /// </summary>
    public abstract IQueryProvider Provider { get; }

    /// <summary>
    /// Adds the given value.
    /// </summary>
    public abstract void Add(T item);

    /// <summary>
    /// Removes the given value.
    /// </summary>
    public abstract void Remove(T item);

    /// <summary>
    /// Finds the value with the given key in the database, or returns null if there is no such value.
    /// </summary>
    public abstract ValueTask<T?> FindAsync(string key);

    /// <summary>
    /// Counts the number of values satisfying the given predicate, or all values if no predicate is provided.
    /// </summary>
    public abstract Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null);

    /// <summary>
    /// See IQueryable.
    /// </summary>
    public abstract IEnumerator<T> GetEnumerator();

    /// <summary>
    /// See IQueryable.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    /// <summary>
    /// See IAsyncEnumerable.
    /// </summary>
    public abstract IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
}