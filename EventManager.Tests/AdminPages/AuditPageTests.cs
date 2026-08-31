using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class AuditPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task ModelIsMessages()
    {
        {
            Db.AuditMessages.Add(
                new AuditMessage(Status.Success, "Hello", null, typeof(AuditPageTests).Name, DateTimeOffset.MinValue),
                new AuditMessage(Status.Success, "World", "alice@example.org", typeof(AuditPageTests).Name, DateTimeOffset.MinValue)
            );
            await Db.CommitAsync();
        }

        var modelAsObject = await new AuditPage(Db.AuditMessages).GetModelAsync(await GetAdminAsync());

        var model = Assert.IsInstanceOfType<IReadOnlyCollection<AuditMessage>>(modelAsObject);
        Assert.AreSequenceEqual(await Db.AuditMessages.ToCollectionAsync(), model);
    }

    [TestMethod]
    public async Task SummaryIsEmpty()
    {
        {
            Db.AuditMessages.Add(
                new AuditMessage(Status.Success, "Hello", "alice@example.org", typeof(AuditPageTests).Name, DateTimeOffset.MinValue)
            );
            await Db.CommitAsync();
        }

        var view = await new AuditPage(Db.AuditMessages).ViewAsync(await GetAdminAsync());

        Assert.IsEmpty(view.Summary);
    }
}