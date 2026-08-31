using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests;

// These tests are solely for coverage purposes; they test scenarios that should never happen.
// This is not great, but it allows us to enforce 100% statement coverage, avoiding any discussion of whether new code should be covered or not.
// Removing these "probably impossible" failure cases from the code would be good, but may need serious refactoring.
[TestClass]
public sealed class CoverageTests : TestsBase
{
    // Note that SystemPages currently "hides" the fact it's not a total function in its source, as the indexer would throw for a nonexistent user type
    [TestMethod]
    public async Task DefaultViewWithoutPage()
    {
        var op = Operation.CreatePageView<User>();
        var pages = new SystemPages(new Dictionary<Type, IReadOnlyCollection<Type>> { { typeof(User), [] } });
        var dependencies = await SystemDependencies.CreateAsync(Db, FileStorage, _ => new FakeEmailSender(false), TimeProvider);
        var result = await op.ExecuteAsync(null, pages, dependencies);
        Assert.AreEqual(Status.SystemError, result.Status);
    }

    // Invariants hold such that we (should) only call this method when it cannot fail
    [TestMethod]
    public async Task DbValuesFirstFailure()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Awards.FirstAsync(a => true));
    }

    // EF Core nonsense
    [TestMethod]
    public async Task DbValuesExtensionsOnNonAsyncEnumerables()
    {
        var queryable = Queryable.AsQueryable<int>([]);
        await Assert.ThrowsAsync<ArgumentException>(() => DbExtensions.FirstAsync(queryable, n => true));
        await Assert.ThrowsAsync<ArgumentException>(() => DbExtensions.FirstOrDefaultAsync(queryable, n => true));
        await Assert.ThrowsAsync<ArgumentException>(() => DbExtensions.ToCollectionAsync(queryable));
        await Assert.ThrowsAsync<ArgumentException>(() => DbExtensions.ToDictionaryAsync(queryable, n => n, EqualityComparer<int>.Default));
    }
}