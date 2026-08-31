using EventManager.Abstractions;

namespace EventManager.Tests.TestInfrastructure;

public static class DbConvenienceExtensions
{
    public static void Add<T>(this DbValues<T> values, params T[] items)
    {
        foreach (var item in items)
        {
            values.Add(item);
        }
    }
}