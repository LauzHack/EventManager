using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

/// <summary>
/// Adds features to IQueryable, and thus also DbValues, instances.
/// </summary>
public static class DbExtensions
{
    /// <summary>
    /// Asynchronously fetches the first result of a query that matches the given filter, if any.
    /// </summary>
    public static async Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> queryable, Expression<Func<T, bool>> filter)
    {
        queryable = queryable.Where(filter);
        var asAsyncEnumerable = queryable as IAsyncEnumerable<T>
                             ?? throw new ArgumentException("Database values should implement IAsyncEnumerable!", nameof(queryable));
        await foreach (var item in asAsyncEnumerable)
        {
            return item;
        }
        return default;
    }

    /// <summary>
    /// Asynchronously fetches the first result of a query that matches the given filter, or throws if there is none.
    /// </summary>
    public static async Task<T> FirstAsync<T>(this IQueryable<T> queryable, Expression<Func<T, bool>> filter)
    {
        queryable = queryable.Where(filter);
        var asAsyncEnumerable = queryable as IAsyncEnumerable<T>
                             ?? throw new ArgumentException("Database values should implement IAsyncEnumerable!", nameof(queryable));
        await foreach (var item in asAsyncEnumerable)
        {
            return item;
        }
        throw new InvalidOperationException("No element matched the predicate");
    }

    /// <summary>
    /// Asynchronously fetches a query's results.
    /// </summary>
    public static async Task<IReadOnlyCollection<T>> ToCollectionAsync<T>(this IQueryable<T> queryable)
    {
        var asAsyncEnumerable = queryable as IAsyncEnumerable<T>
                             ?? throw new ArgumentException("Database values should implement IAsyncEnumerable!", nameof(queryable));
        var list = new List<T>();
        await foreach (var item in asAsyncEnumerable)
        {
            list.Add(item);
        }
        return list;
    }

    /// <summary>
    /// Asynchronously fetches a query's results into a dictionary, using the given key selector.
    /// </summary>
    public static async Task<IReadOnlyDictionary<TKey, T>> ToDictionaryAsync<T, TKey>(this IQueryable<T> queryable, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var asAsyncEnumerable = queryable as IAsyncEnumerable<T>
                             ?? throw new ArgumentException("Database values should implement IAsyncEnumerable!", nameof(queryable));
        var result = new Dictionary<TKey, T>(comparer);
        await foreach (var item in asAsyncEnumerable)
        {
            result.Add(keySelector(item), item);
        }
        return result;
    }
}