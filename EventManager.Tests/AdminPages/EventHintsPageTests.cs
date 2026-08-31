using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class EventHintsPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsAlwaysOptional()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventHintsPage(new ConfigValue<EventHints>(config));

        var result = await page.ViewAsync(await GetAdminAsync());

        Assert.IsFalse(result.IsRequired);
        Assert.IsTrue(result.IsInteractable);
    }

    [TestMethod]
    public async Task EditSetsConfiguration()
    {
        var customHints = new EventHints([new("X", "Line", "Second")], "", [new("Y", "Other", "")]);

        {
            var config = await Config.CreateAsync(Db);
            Assert.AreNotEqual(customHints.PresentationHintsHeader, config.EventHints.PresentationHintsHeader);
            var page = new EventHintsPage(new ConfigValue<EventHints>(config));

            var result = await page.EditAsync(customHints);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var config2 = await Config.CreateAsync(Db);
        // ImmutableArray has reference equality :(
        Assert.AreSequenceEqual(customHints.ApplicationHints, config2.EventHints.ApplicationHints);
        Assert.AreEqual(customHints.PresentationHintsHeader, config2.EventHints.PresentationHintsHeader);
        Assert.AreSequenceEqual(customHints.PresentationHints, config2.EventHints.PresentationHints);
    }
}