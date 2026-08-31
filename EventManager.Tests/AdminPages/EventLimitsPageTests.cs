using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class EventLimitsPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenLimitsAreMissing()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventLimitsPage(new ConfigValue<EventLimits>(config));

        var result = await page.ViewAsync(await GetAdminAsync());

        Assert.IsTrue(result.IsRequired);
        Assert.IsTrue(result.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PageIsOptionalWhenLimitsExist(bool isOwner)
    {
        await SetConfigValueAsync(EventLimits);

        var config = await Config.CreateAsync(Db);
        var page = new EventLimitsPage(new ConfigValue<EventLimits>(config));

        var admin = isOwner ? await GetAdminAsync()
                            : await CreateNonOwnerAdminAsync();
        var result = await page.ViewAsync(admin);

        Assert.IsFalse(result.IsRequired);
        Assert.AreEqual(isOwner, result.IsInteractable);
    }

    [TestMethod]
    [DataRow(0u, 4u)]
    [DataRow(1u, 4u)]
    [DataRow(3u, 7u)]
    [DataRow(4u, 0u)]
    public async Task SummaryContainsGroupAndTeamSizesOrWordsForDisabled(uint groupSize, uint teamSize)
    {
        await SetConfigValueAsync(EventLimits with { ApplicationGroupSize = groupSize, ProjectTeamSize = teamSize });

        var config = await Config.CreateAsync(Db);
        var page = new EventLimitsPage(new ConfigValue<EventLimits>(config));

        var view = await page.ViewAsync(await GetAdminAsync());

        if (groupSize > 1)
        {
            Assert.AreEqual(
                groupSize.ToString(CultureInfo.InvariantCulture),
                view.Summary.First(s => s.Label.Equals("Application group size", StringComparison.Ordinal)).Text,
                StringComparer.Ordinal
            );
        }
        else
        {
            Assert.AreEqual("Alone", view.Summary.First(s => s.Label.Equals("Applications", StringComparison.Ordinal)).Text, StringComparer.Ordinal);
        }

        if (teamSize >= 1)
        {
            Assert.AreEqual(
                teamSize.ToString(CultureInfo.InvariantCulture),
                view.Summary.First(s => s.Label.Equals("Project team size", StringComparison.Ordinal)).Text,
                StringComparer.Ordinal
            );
        }
        else
        {
            Assert.AreEqual("Disabled", view.Summary.First(s => s.Label.Equals("Projects", StringComparison.Ordinal)).Text, StringComparer.Ordinal);
        }
    }

    [TestMethod]
    public async Task EditSetsConfiguration()
    {
        {
            var config = await Config.CreateAsync(Db);
            var page = new EventLimitsPage(new ConfigValue<EventLimits>(config));

            var result = await page.EditAsync(EventLimits);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var config2 = await Config.CreateAsync(Db);
        Assert.AreEqual(EventLimits, config2.EventLimits);
    }

    [TestMethod]
    public async Task EditFailsWhenDaysBetweenRemindersIsZero()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventLimitsPage(new ConfigValue<EventLimits>(config));

        var result = await page.EditAsync(EventLimits with { DaysBetweenReminders = 0 });
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsWhenDaysToConfirmIsZero()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventLimitsPage(new ConfigValue<EventLimits>(config));

        var result = await page.EditAsync(EventLimits with { DaysToConfirm = 0 });
        Assert.AreEqual(Status.UserError, result.Status);
    }
}