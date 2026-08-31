using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class CheckInPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhileCheckInHasNotEnded()
    {
        var config = await Config.CreateAsync(Db);
        var view = await GetPage(config, disableEmails: true).ViewAsync(await GetAdminAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableOnceCheckInHasEnded()
    {
        await SetConfigValueAsync(EventStatus.CheckInClosed);

        var config = await Config.CreateAsync(Db);
        var view = await GetPage(config, disableEmails: true).ViewAsync(await GetAdminAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task ModelIsConfirmedAndCheckedInParticipants()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org"),
                new Participant("bob@example.org") { Status = ParticipantStatus.Accepted },
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed },
                new Participant("eve@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var modelAsObject = await GetPage(config, disableEmails: true).GetModelAsync(await GetAdminAsync());

        var model = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(modelAsObject);
        Assert.AreSequenceEqual(["carol@example.org", "daniel@example.org"], model.Select(p => p.EmailAddress));
    }

    [TestMethod]
    public async Task SummaryIsEmpty()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org"),
                new Participant("bob@example.org") { Status = ParticipantStatus.Accepted },
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed },
                new Participant("eve@example.org") { Status = ParticipantStatus.CheckedIn }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var view = await GetPage(config, disableEmails: true).ViewAsync(await GetAdminAsync());

        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    public async Task CheckInSetsStatusToCheckedIn()
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var result = await GetPage(config, disableEmails: true).CheckInAsync("daniel@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("daniel@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual(ParticipantStatus.CheckedIn, participant.Status);
    }

    [TestMethod]
    public async Task CheckInIncludesAdminRemarksInMessage()
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed, AdminRemarks = "Hello, World!" }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var result = await GetPage(config, disableEmails: true).CheckInAsync("daniel@example.org");

        Assert.AreEqual(Status.Success, result.Status);
        Assert.Contains("Hello, World!", result.Text, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CheckInFailsWhenAlreadyCheckedIn()
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var result = await GetPage(config, disableEmails: true).CheckInAsync("carol@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CheckInFailsWhenNotConfirmed()
    {
        {
            Db.Participants.Add(
                new Participant("bob@example.org") { Status = ParticipantStatus.Accepted },
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var result = await GetPage(config, disableEmails: true).CheckInAsync("bob@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CheckInFailsForUnknownEmailAddress()
    {
        {
            Db.Participants.Add(
                new Participant("bob@example.org") { Status = ParticipantStatus.Accepted },
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var result = await GetPage(config, disableEmails: true).CheckInAsync("xyzzy@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelCheckInSetsStatusToConfirmed()
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var result = await GetPage(config, disableEmails: true).CancelCheckInAsync("carol@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("carol@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual(ParticipantStatus.Confirmed, participant.Status);
    }

    [TestMethod]
    public async Task CancelCheckInReturnsErrorWhenNotCheckedIn()
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var result = await GetPage(config, disableEmails: true).CancelCheckInAsync("daniel@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelCheckInFailsForUnknownEmailAddress()
    {
        {
            Db.Participants.Add(
                new Participant("bob@example.org") { Status = ParticipantStatus.Accepted },
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var result = await GetPage(config, disableEmails: true).CancelCheckInAsync("xyzzy@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow("Apple")]
    [DataRow(null)]
    public async Task CheckInUnknownCreatesCheckedInUser(string? familyName)
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var result = await GetPage(config).CheckInUnknownAsync("alice@example.org", "Alice", familyName);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual("Alice", participant.GivenName);
        Assert.AreEqual(familyName, participant.FamilyName);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(participant.EmailAddress, email.Recipient);
        Assert.AreEqual(Operation.CreatePageView<Participant>(), email.Operation);
    }

    [TestMethod]
    [DataRow("Normal")]
    [DataRow(null)]
    public async Task CheckInUnknownChecksInExistingUser(string? familyName)
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed },
                new Participant("eve@example.org") { GivenName = "Eve", FamilyName = "Eevee", Status = ParticipantStatus.Accepted }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var result = await GetPage(config).CheckInUnknownAsync("eve@example.org", "Eevee", familyName);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("eve@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual("Eevee", participant.GivenName);
        Assert.AreEqual(familyName, participant.FamilyName);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(participant.EmailAddress, email.Recipient);
        Assert.AreEqual(Operation.CreatePageView<Participant>(), email.Operation);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.CheckedIn)]
    [DataRow(ParticipantStatus.DeclaredTravelExpenses)]
    public async Task CheckInUnknownFailsIfUserExistsAndIsAlreadyCheckedIn(ParticipantStatus status)
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed },
                new Participant("eve@example.org") { GivenName = "Eve", FamilyName = "Eevee", Status = status }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var result = await GetPage(config).CheckInUnknownAsync("eve@example.org", "Eve", "Eggplant");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task FinishCheckInSetsStatusToCheckInClosed()
    {
        {
            var config = await Config.CreateAsync(Db);
            var result = await GetPage(config, disableEmails: true).FinishCheckInAsync(await GetAdminAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newConfig = await Config.CreateAsync(Db);
        Assert.AreEqual(EventStatus.CheckInClosed, newConfig.EventStatus);
    }

    [TestMethod]
    public async Task FinishCheckInFailsIfAdminIsNotOwner()
    {
        await SetConfigValueAsync(EventStatus.CheckInStarted);

        var notOwner = await CreateNonOwnerAdminAsync();

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config, disableEmails: true);
        var result = await page.FinishCheckInAsync(notOwner);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RestartCheckInSetsStatusToCheckInStarted()
    {
        {
            var config = await Config.CreateAsync(Db);
            var result = await GetPage(config, disableEmails: true).RestartCheckInAsync(await GetAdminAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newConfig = await Config.CreateAsync(Db);
        Assert.AreEqual(EventStatus.CheckInStarted, newConfig.EventStatus);
    }

    [TestMethod]
    public async Task RestartCheckInFailsIfAdminIsNotOwner()
    {
        await SetConfigValueAsync(EventStatus.CheckInClosed);

        var notOwner = await CreateNonOwnerAdminAsync();

        var config = await Config.CreateAsync(Db);
        var page = GetPage(config, disableEmails: true);
        var result = await page.RestartCheckInAsync(notOwner);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    private CheckInPage GetPage(Config config, bool disableEmails = false)
        => new(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), disableEmails ? DisabledEmailSender : EmailSender, TimeProvider);
}