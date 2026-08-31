using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class EndPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequired()
    {
        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task SendLoginEmailToCheckedInDoesJustThat()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed },
                new Participant("bob@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config, enableEmails: true);

        var result = await page.SendLoginEmailToCheckedInAsync();
        Assert.AreEqual(Status.Success, result.Status);

        Assert.HasCount(2, EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", EmailSender.Outbox[0].Recipient);
        Assert.AreEqual("carol@example.org", EmailSender.Outbox[1].Recipient);
        Assert.AreEqual(Operation.CreatePageView<Participant>(), EmailSender.Outbox[0].Operation);
    }

    [TestMethod]
    public async Task StartJudgingDoesJustThat()
    {
        await SetConfigValueAsync(EventStatus.CheckInClosed);

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.StartJudgingAsync(await GetAdminAsync());
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreEqual(EventStatus.JudgingStarted, config.EventStatus);
    }

    [TestMethod]
    public async Task StartJudgingFailsForNonOwner()
    {
        await SetConfigValueAsync(EventStatus.CheckInClosed);

        var notOwner = await CreateNonOwnerAdminAsync();
        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.StartJudgingAsync(notOwner);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task StartJudgingFailsIfNotInCheckInClosedState()
    {
        await SetConfigValueAsync(EventStatus.CheckInStarted);

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.StartJudgingAsync(await GetAdminAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelJudgingStartDoesJustThat()
    {
        await SetConfigValueAsync(EventStatus.JudgingStarted);

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.CancelJudgingStartAsync(await GetAdminAsync());
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreEqual(EventStatus.CheckInClosed, config.EventStatus);
    }

    [TestMethod]
    public async Task CancelJudgingStartFailsForNonOwner()
    {
        await SetConfigValueAsync(EventStatus.JudgingStarted);

        var notOwner = await CreateNonOwnerAdminAsync();
        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.CancelJudgingStartAsync(notOwner);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelJudgingStartFailsIfJudgingHasNotStarted()
    {
        await SetConfigValueAsync(EventStatus.CheckInClosed);

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.CancelJudgingStartAsync(await GetAdminAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EndJudgingDoesJustThat()
    {
        await SetConfigValueAsync(EventStatus.JudgingStarted);

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.EndJudgingAsync(await GetAdminAsync());
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreEqual(EventStatus.Finished, config.EventStatus);
    }

    [TestMethod]
    public async Task EndJudgingFailsForNonOwner()
    {
        await SetConfigValueAsync(EventStatus.JudgingStarted);

        var notOwner = await CreateNonOwnerAdminAsync();
        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.EndJudgingAsync(notOwner);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EndJudgingFailsIfNotInJudgingStartedState()
    {
        await SetConfigValueAsync(EventStatus.CheckInClosed);

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.EndJudgingAsync(await GetAdminAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelJudgingEndDoesJustThat()
    {
        await SetConfigValueAsync(EventStatus.Finished);

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.CancelJudgingEndAsync(await GetAdminAsync());
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreEqual(EventStatus.JudgingStarted, config.EventStatus);
    }

    [TestMethod]
    public async Task CancelJudgingEndFailsForNonOwner()
    {
        await SetConfigValueAsync(EventStatus.Finished);

        var notOwner = await CreateNonOwnerAdminAsync();
        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.CancelJudgingEndAsync(notOwner);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelJudgingEndFailsIfJudgingHasNotEnded()
    {
        await SetConfigValueAsync(EventStatus.JudgingStarted);

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config);

        var result = await page.CancelJudgingEndAsync(await GetAdminAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    private EndPage GetPage(Config config, bool enableEmails = false)
        => new(Db.Participants, Db.ChallengeSetters, new ConfigValue<EventStatus>(config), enableEmails ? EmailSender : DisabledEmailSender);
}