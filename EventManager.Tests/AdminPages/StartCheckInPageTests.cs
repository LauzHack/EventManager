using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class StartCheckInPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenApplicationsAreClosed()
    {
        var config = await Config.CreateAsync(Db);
        config.Set(EventStatus.ApplicationsClosed);
        await Db.CommitAsync();

        var page = new StartCheckInPage(new ConfigValue<EventStatus>(config));

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsHiddenOnceCheckInHasStarted()
    {
        var config = await Config.CreateAsync(Db);
        config.Set(EventStatus.CheckInStarted);
        await Db.CommitAsync();

        var page = new StartCheckInPage(new ConfigValue<EventStatus>(config));

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task StartIndeedStartsCheckIn()
    {
        var config = await Config.CreateAsync(Db);
        var page = new StartCheckInPage(new ConfigValue<EventStatus>(config));

        var result = await page.StartAsync(await GetAdminAsync());
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreEqual(EventStatus.CheckInStarted, config.EventStatus);
    }

    [TestMethod]
    public async Task StartFailsForNonOwner()
    {
        var config = await Config.CreateAsync(Db);
        var page = new StartCheckInPage(new ConfigValue<EventStatus>(config));

        var result = await page.StartAsync(await CreateNonOwnerAdminAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }
}