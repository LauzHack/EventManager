using System.Collections.Immutable;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class TravelReimbursementPolicyPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenOnceCheckInHasStartedIfNoPolicyIsConfigured()
    {
        var config = await Config.CreateAsync(Db);
        var page = new TravelReimbursementPolicyPage(Db.Participants, new ConfigValue<TravelReimbursementPolicy>(config), EventStatus.CheckInStarted);

        var result = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(result.IsRequired);
        Assert.IsFalse(result.IsInteractable);
        Assert.IsEmpty(result.Summary);
    }

    [TestMethod]
    public async Task PageIsSummaryOnlyOnceCheckInHasStartedWhenAPolicyIsConfigured()
    {
        var config = await Config.CreateAsync(Db);
        config.Set(ReimbursementPolicy);
        var page = new TravelReimbursementPolicyPage(Db.Participants, new ConfigValue<TravelReimbursementPolicy>(config), EventStatus.CheckInStarted);

        var result = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(result.IsRequired);
        Assert.IsFalse(result.IsInteractable);
        Assert.IsNotEmpty(result.Summary);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task PageIsEditableWhenCheckInHasNotStartedForOwners(bool alreadyConfigured, bool isOwner)
    {
        var config = await Config.CreateAsync(Db);
        if (alreadyConfigured)
        {
            config.Set(ReimbursementPolicy);
        }
        var page = new TravelReimbursementPolicyPage(Db.Participants, new ConfigValue<TravelReimbursementPolicy>(config), EventStatus.ApplicationsClosed);

        var admin = isOwner ? await GetAdminAsync()
                            : await CreateNonOwnerAdminAsync();
        var result = await page.ViewAsync(admin);

        Assert.IsFalse(result.IsRequired);
        Assert.AreEqual(isOwner, result.IsInteractable);
        if (alreadyConfigured)
        {
            Assert.IsNotEmpty(result.Summary);
        }
        else
        {
            Assert.IsEmpty(result.Summary);
        }
    }

    [TestMethod]
    public async Task SetDoesSo()
    {
        var config = await Config.CreateAsync(Db);
        var page = new TravelReimbursementPolicyPage(Db.Participants, new ConfigValue<TravelReimbursementPolicy>(config), EventStatus.Configuring);

        var result = await page.EditAsync(new("CHF", "Descr", new("https://example.org"), ImmutableDictionary.CreateRange<string, decimal>([new("A", 42m), new("BB", 123.56m), new("Nothing", 0m)]), 1));
        await Db.CommitAsync();
        Assert.AreEqual(Status.Success, result.Status);
        Assert.IsNotNull(config.TravelReimbursementPolicy);
        Assert.AreEqual("CHF", config.TravelReimbursementPolicy.EventCurrencyCode);
        Assert.AreEqual("Descr", config.TravelReimbursementPolicy.TiersDescription);
        Assert.AreEqual(new("https://example.org"), config.TravelReimbursementPolicy.DetailsUrl);
        Assert.AreEquivalent([new("A", 42m), new("BB", 123.56m), new("Nothing", 0m)], config.TravelReimbursementPolicy.Tiers);

        // the summary should have them in increasing order
        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.HasCount(4, view.Summary);
        Assert.AreEqual("Nothing", view.Summary[0].Label);
        Assert.AreEqual("CHF 0", view.Summary[0].Text);
        Assert.AreEqual("A", view.Summary[1].Label);
        Assert.AreEqual("CHF 42", view.Summary[1].Text);
        Assert.AreEqual("BB", view.Summary[2].Label);
        Assert.AreEqual("CHF 123.56", view.Summary[2].Text);
        Assert.AreEqual("Rounding amount", view.Summary[3].Label);
        Assert.AreEqual("CHF 1", view.Summary[3].Text);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SetClearsTiersThatNoLongerExist(bool areInUse)
    {
        // The config is set
        {
            var config = await Config.CreateAsync(Db);
            var page = new TravelReimbursementPolicyPage(Db.Participants, new ConfigValue<TravelReimbursementPolicy>(config), EventStatus.Configuring);
            var result = await page.EditAsync(new("CHF", "Descr", new("https://example.org"), ImmutableDictionary.CreateRange<string, decimal>([new("First", 20m), new("Second", 50m)]), 1));
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        // Participants apply using this config
        {
            Db.Participants.Add(
                new("alice@example.org") { GivenName = "Alice", Status = ParticipantStatus.Confirmed, TravelReimbursementTier = "First" },
                new("daniel@example.org") { GivenName = "Daniel", Status = ParticipantStatus.Accepted }
            );
            if (areInUse)
            {
                Db.Participants.Add(
                    new("bob@example.org") { GivenName = "Bob", Status = ParticipantStatus.Confirmed, TravelReimbursementTier = "Second" },
                    new("carol@example.org") { GivenName = "Carol", Status = ParticipantStatus.Confirmed, TravelReimbursementTier = "Second" }
                );
            }
            await Db.CommitAsync();
        }

        // The config is changed, "First" has the amount changed, "Second" disappears, and "Third" appears
        {
            var config = await Config.CreateAsync(Db);
            var page = new TravelReimbursementPolicyPage(Db.Participants, new ConfigValue<TravelReimbursementPolicy>(config), EventStatus.Configuring);
            var result = await page.EditAsync(new("CHF", "Descr2", new("https://example.org/2"), ImmutableDictionary.CreateRange<string, decimal>([new("First", 25m), new("Third", 100m)]), 5));
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var alice = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(alice);
        Assert.AreEqual("First", alice.TravelReimbursementTier);

        if (areInUse)
        {
            var bob = await Db.Participants.FindAsync("bob@example.org");
            Assert.IsNotNull(bob);
            Assert.IsNull(bob.TravelReimbursementTier);

            var carol = await Db.Participants.FindAsync("carol@example.org");
            Assert.IsNotNull(carol);
            Assert.IsNull(carol.TravelReimbursementTier);
        }

        var daniel = await Db.Participants.FindAsync("daniel@example.org");
        Assert.IsNotNull(daniel);
        Assert.IsNull(daniel.TravelReimbursementTier);
    }

    [TestMethod]
    public async Task SetFailsWithoutTiers()
    {
        var config = await Config.CreateAsync(Db);
        var page = new TravelReimbursementPolicyPage(Db.Participants, new ConfigValue<TravelReimbursementPolicy>(config), EventStatus.Configuring);

        var result = await page.EditAsync(new("CHF", "Descr", new("https://example.org"), [], 1));
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SetFailsWithAmountsLessThanZero()
    {
        var config = await Config.CreateAsync(Db);
        var page = new TravelReimbursementPolicyPage(Db.Participants, new ConfigValue<TravelReimbursementPolicy>(config), EventStatus.Configuring);

        var result = await page.EditAsync(new("CHF", "Descr", new("https://example.org"), ImmutableDictionary.CreateRange<string, decimal>([new("A", 1m), new("BB", -42m)]), 1));
        Assert.AreEqual(Status.UserError, result.Status);
    }
}