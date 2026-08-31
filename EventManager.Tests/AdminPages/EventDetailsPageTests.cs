using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class EventDetailsPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenDetailsAreMissing()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventDetailsPage(new ConfigValue<EventDetails>(config));

        var result = await page.ViewAsync(await GetAdminAsync());

        Assert.IsTrue(result.IsRequired);
        Assert.IsTrue(result.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PageIsOptionalWhenDetailsExist(bool isOwner)
    {
        await SetConfigValueAsync(EventDetails);

        var config = await Config.CreateAsync(Db);
        var page = new EventDetailsPage(new ConfigValue<EventDetails>(config));

        var admin = isOwner ? await GetAdminAsync()
                            : await CreateNonOwnerAdminAsync();
        var result = await page.ViewAsync(admin);

        Assert.IsFalse(result.IsRequired);
        Assert.AreEqual(isOwner, result.IsInteractable);
    }

    [TestMethod]
    public async Task EditSetsConfiguration()
    {
        {
            var config = await Config.CreateAsync(Db);
            var page = new EventDetailsPage(new ConfigValue<EventDetails>(config));

            var result = await page.EditAsync(EventDetails);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var config2 = await Config.CreateAsync(Db);
        Assert.AreEqual(EventDetails, config2.EventDetails);
    }

    [TestMethod]
    public async Task ErrorWhenEndIsAfterStart()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventDetailsPage(new ConfigValue<EventDetails>(config));

        var result = await page.EditAsync(EventDetails with { End = EventDetails.Start.AddDays(-1) });
        Assert.AreEqual(Status.UserError, result.Status);
    }
}