using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class OpenApplicationsPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenEventIsBeingConfigured()
    {
        var config = await Config.CreateAsync(Db);
        var page = new OpenApplicationsPage(new ConfigValue<EventStatus>(config));

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsHiddenOnceApplicationsAreOpen()
    {
        var config = await Config.CreateAsync(Db);
        config.Set(EventStatus.ApplicationsOpen);
        await Db.CommitAsync();

        var page = new OpenApplicationsPage(new ConfigValue<EventStatus>(config));

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task OpenIndedOpensApplications()
    {
        var config = await Config.CreateAsync(Db);
        var page = new OpenApplicationsPage(new ConfigValue<EventStatus>(config));

        var result = await page.OpenAsync(await GetAdminAsync());
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreEqual(EventStatus.ApplicationsOpen, config.EventStatus);
    }

    [TestMethod]
    public async Task OpenFailsForNonOwner()
    {
        var config = await Config.CreateAsync(Db);
        var page = new OpenApplicationsPage(new ConfigValue<EventStatus>(config));

        var result = await page.OpenAsync(await CreateNonOwnerAdminAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }
}