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
public sealed class VisaInvitationLettersPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsSummaryOnlyWithoutLetterData()
    {
        var config = await Config.CreateAsync(Db);
        var page = new VisaInvitationLettersPage(Db.Participants, null, new ConfigValue<VisaInvitationFormat>(config), DisabledEmailSender, DisabledTimeProvider);

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsNotEmpty(view.Summary);
    }

    [TestMethod]
    public async Task PageIsEditableWithLetterDataButWithoutVisaFormat()
    {
        await SetConfigValueAsync(LetterData);

        var config = await Config.CreateAsync(Db);
        var page = new VisaInvitationLettersPage(Db.Participants, config.LetterData, new ConfigValue<VisaInvitationFormat>(config), DisabledEmailSender, DisabledTimeProvider);

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWithLetterDataAndVisaFormat()
    {
        var page = await CreatePageAsync(disableEmail: true, disableTime: true);

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task ModelIsParticipantsWithVisaInformationWithAlreadyIdentifiedLast()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    VisaInformation = new ParticipantVisaInformation
                    {
                        PassportPhotoId = "xxx",
                        AdminDetails = "id",
                        Letter = new Letter("id", "hello", DateTimeOffset.MinValue)
                    }
                },
                new Participant("bob@example.org"),
                new Participant("carol@example.org")
                {
                    VisaInformation = new ParticipantVisaInformation
                    {
                        PassportPhotoId = "yyy"
                    }
                }
            );
            await Db.CommitAsync();
        }

        var page = await CreatePageAsync(disableEmail: true, disableTime: true);
        var modelAsObject = await page.GetModelAsync(await GetAdminAsync());

        var model = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(modelAsObject);
        Assert.AreSequenceEqual(["carol@example.org", "alice@example.org"], model.Select(p => p.EmailAddress));
    }

    [TestMethod]
    public async Task SummaryIsCountOfParticipantsNeedingReviewWhenThereAreSome()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    VisaInformation = new ParticipantVisaInformation
                    { PassportPhotoId = "xxx" }
                },
                new Participant("bob@example.org"),
                new Participant("carol@example.org")
                {
                    VisaInformation = new ParticipantVisaInformation
                    {
                        PassportPhotoId = "yyy",
                        Letter = new Letter("id", "hello", DateTimeOffset.MinValue)
                    }
                }
            );
            await Db.CommitAsync();
        }

        var page = await CreatePageAsync(disableEmail: true, disableTime: true);
        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.Contains("1", view.Summary[0].Text, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task SetFormatIndeedSetsFormat()
    {
        await SetConfigValueAsync(LetterData);

        var config = await Config.CreateAsync(Db);
        var page = new VisaInvitationLettersPage(Db.Participants, config.LetterData, new ConfigValue<VisaInvitationFormat>(config), DisabledEmailSender, DisabledTimeProvider);
        var result = await page.SetFormatAsync(await GetAdminAsync(), VisaInvitationFormat);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var newConfig = await Config.CreateAsync(Db);
        Assert.IsNotNull(newConfig.VisaInvitationFormat);
        // ImmutableArray has reference equality :(
        Assert.AreEqual(VisaInvitationFormat.Template, newConfig.VisaInvitationFormat.Template);
        Assert.AreSequenceEqual(VisaInvitationFormat.ParticipantDetails, newConfig.VisaInvitationFormat.ParticipantDetails);
        Assert.AreEqual(VisaInvitationFormat.AdminDetails, newConfig.VisaInvitationFormat.AdminDetails);
    }

    [TestMethod]
    public async Task SetFormatFailsForNonOwner()
    {
        await SetConfigValueAsync(LetterData);

        var config = await Config.CreateAsync(Db);
        var page = new VisaInvitationLettersPage(Db.Participants, config.LetterData, new ConfigValue<VisaInvitationFormat>(config), DisabledEmailSender, DisabledTimeProvider);

        var result = await page.SetFormatAsync(await CreateNonOwnerAdminAsync(), VisaInvitationFormat);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow("Neither")]
    [DataRow($"Just{VisaInvitationFormat.TemplateNamePlaceholder}")]
    [DataRow($"Just{VisaInvitationFormat.TemplateDetailsPlaceholder}")]
    public async Task SetFormatFailsIfPlaceholdersAreNotPresent(string text)
    {
        await SetConfigValueAsync(LetterData);

        var config = await Config.CreateAsync(Db);
        var page = new VisaInvitationLettersPage(Db.Participants, config.LetterData, new ConfigValue<VisaInvitationFormat>(config), DisabledEmailSender, DisabledTimeProvider);

        var result = await page.SetFormatAsync(await GetAdminAsync(), new(text, ["a", "b", "c"], "xyz"));
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task AcceptCreatesLetterWhenNoneExists()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    GivenName = "Alice",
                    VisaInformation = new ParticipantVisaInformation
                    {
                        PassportPhotoId = "xxx"
                    }
                }
            );
            await Db.CommitAsync();
        }

        var identity = "born January 1st, 1800";
        {
            var page = await CreatePageAsync();
            var result = await page.AcceptAsync("alice@example.org", identity);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual(identity, participant.VisaInformation.AdminDetails);
        Assert.IsNotNull(participant.VisaInformation.Letter);
        Assert.AreEqual("Hello Alice, born January 1st, 1800", participant.VisaInformation.Letter.Body);
    }

    [TestMethod]
    public async Task AcceptModifiesLetterWhenOneExists()
    {
        {
            Db.Participants.Add(
                new Participant("carol@example.org")
                {
                    GivenName = "Carol",
                    FamilyName = "Coconut",
                    VisaInformation = new ParticipantVisaInformation
                    {
                        PassportPhotoId = "xxx",
                        Letter = new Letter("id", "hello", DateTimeOffset.MinValue)
                    }
                }
                );
            await Db.CommitAsync();
        }

        var identity = "born June 30th, 1900";
        {
            var page = await CreatePageAsync();
            var result = await page.AcceptAsync("carol@example.org", identity);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("carol@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual(identity, participant.VisaInformation.AdminDetails);
        Assert.IsNotNull(participant.VisaInformation.Letter);
        Assert.AreEqual("Hello Carol Coconut, born June 30th, 1900", participant.VisaInformation.Letter.Body);
    }

    [TestMethod]
    public async Task AcceptSendsEmail()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    GivenName = "Alice",
                    VisaInformation = new ParticipantVisaInformation { PassportPhotoId = "xxx" }
                }
            );
            await Db.CommitAsync();
        }

        var identity = "Alice Apple, born January 1st, 1800";
        var page = await CreatePageAsync();
        var result = await page.AcceptAsync("alice@example.org", identity);

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", email.Recipient);
    }

    [TestMethod]
    public async Task AcceptFailsForParticipantWithoutName()
    {
        {
            Db.Participants.Add(
                new Participant("bob@example.org")
            );
            await Db.CommitAsync();
        }

        {
            var page = await CreatePageAsync(disableEmail: true, disableTime: true);
            var result = await page.AcceptAsync("bob@example.org", "Bob Banana, born December 1st, 2000");
            Assert.AreEqual(Status.UserError, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(participant);
        Assert.IsNull(participant.VisaInformation.PassportPhotoId);
    }

    [TestMethod]
    public async Task AcceptFailsForParticipantWithoutVisaInformation()
    {
        {
            Db.Participants.Add(
                new Participant("bob@example.org")
                {
                    GivenName = "Bob"
                }
            );
            await Db.CommitAsync();
        }

        {
            var page = await CreatePageAsync(disableEmail: true, disableTime: true);
            var result = await page.AcceptAsync("bob@example.org", "Bob Banana, born December 1st, 2000");
            Assert.AreEqual(Status.UserError, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(participant);
        Assert.IsNull(participant.VisaInformation.PassportPhotoId);
    }

    [TestMethod]
    public async Task AcceptFailsForUnknownEmailAddress()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    GivenName = "Alice",
                    VisaInformation = new ParticipantVisaInformation { PassportPhotoId = "xxx" }
                }
            );
            await Db.CommitAsync();
        }

        {
            var page = await CreatePageAsync(disableEmail: true, disableTime: true);
            var result = await page.AcceptAsync("daniel@example.org", "Daniel Dragonfruit, born October 10th, 1582");
            Assert.AreEqual(Status.UserError, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.IsNull(participant.VisaInformation.Letter);
    }

    [TestMethod]
    public async Task AcceptFailsWithoutVisaFormat()
    {
        await SetConfigValueAsync(LetterData);

        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    GivenName = "Alice",
                    VisaInformation = new ParticipantVisaInformation { PassportPhotoId = "xxx" }
                }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new VisaInvitationLettersPage(Db.Participants, config.LetterData, new ConfigValue<VisaInvitationFormat>(config), DisabledEmailSender, DisabledTimeProvider);
        var result = await page.AcceptAsync("alice@example.org", "Alice Apple, born January 1st, 1800");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RejectClearsInformation()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    GivenName = "Alice",
                    VisaInformation = new ParticipantVisaInformation { PassportPhotoId = "xxx" }
                }
            );
            await Db.CommitAsync();
        }

        {
            var page = await CreatePageAsync(disableTime: true);
            var result = await page.RejectAsync("alice@example.org", "rejecting you for testing");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.IsNull(participant.VisaInformation.PassportPhotoId);
    }

    [TestMethod]
    public async Task RejectSendsEmail()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    GivenName = "Alice",
                    VisaInformation = new ParticipantVisaInformation { PassportPhotoId = "xxx" }
                }
            );
            await Db.CommitAsync();
        }

        var reason = "rejecting you for testing";
        var page = await CreatePageAsync(disableTime: true);
        var result = await page.RejectAsync("alice@example.org", reason);

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", email.Recipient);
        Assert.Contains(reason, EmailSender.Outbox[0].Body, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RejectFailsForParticipantWithoutVisaInformation()
    {
        {
            Db.Participants.Add(
                new Participant("bob@example.org")
                {
                    GivenName = "Bob"
                }
            );
            await Db.CommitAsync();
        }

        var page = await CreatePageAsync(disableEmail: true, disableTime: true);
        var result = await page.RejectAsync("bob@example.org", "why not");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RejectFailsForUnknownEmailAddress()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    GivenName = "Alice",
                    VisaInformation = new ParticipantVisaInformation { PassportPhotoId = "xxx" }
                }
            );
            await Db.CommitAsync();
        }

        var page = await CreatePageAsync(disableEmail: true, disableTime: true);
        var result = await page.RejectAsync("daniel@example.org", "you don't exist");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    private async Task<VisaInvitationLettersPage> CreatePageAsync(bool disableEmail = false, bool disableTime = false)
    {
        await SetConfigValueAsync(LetterData);
        await SetConfigValueAsync(VisaInvitationFormat);
        var config = await Config.CreateAsync(Db);
        return new VisaInvitationLettersPage(
            Db.Participants,
            config.LetterData,
            new ConfigValue<VisaInvitationFormat>(config),
            disableEmail ? DisabledEmailSender : EmailSender,
            disableTime ? DisabledTimeProvider : TimeProvider
        );
    }
}