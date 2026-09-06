using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class EmailSetupPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsEditableButNotRequiredWhenNotConfigured()
    {
        {
            var admin = await GetAdminAsync();
            admin.IsEmailAddressVerified = true;
            var config = await Config.CreateAsync(Db);
            config.Set(new AuthenticationSecret([0, 1, 2, 3]));
            config.Set(new EmailSenderSettings(new Uri("smtp://example.com:587", UriKind.Absolute), "x", "x", "x", "y@example.org", "z@example.org"));
            await Db.CommitAsync();
        }

        var page = await CreatePageAsync();
        var result = await page.ViewAsync(await GetAdminAsync());

        Assert.IsFalse(result.IsRequired);
        Assert.IsTrue(result.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsForbiddenWithoutAdminOnceConfigured()
    {
        {
            var admin = await GetAdminAsync();
            admin.IsEmailAddressVerified = true;
            var config = await Config.CreateAsync(Db);
            config.Set(new AuthenticationSecret([0, 1, 2, 3]));
            config.Set(new EmailSenderSettings(new Uri("smtp://example.com:587", UriKind.Absolute), "x", "x", "x", "y@example.org", "z@example.org"));
            await Db.CommitAsync();
        }

        var page = await CreatePageAsync();
        var result = await page.ViewAsync(null);

        Assert.IsFalse(result.IsRequired);
        Assert.IsFalse(result.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsRequiredWithoutAnything()
    {
        var page = await CreatePageAsync();
        var result = await page.ViewAsync(null);

        Assert.IsTrue(result.IsRequired);
        Assert.IsTrue(result.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PageIsEditableToOwnersOnlyOnceConfigured(bool isOwner)
    {
        {
            var admin = await GetAdminAsync();
            admin.IsEmailAddressVerified = true;
            var config = await Config.CreateAsync(Db);
            config.Set(new AuthenticationSecret([0, 1, 2, 3]));
            config.Set(new EmailSenderSettings(new Uri("smtp://example.com:587", UriKind.Absolute), "x", "x", "x", "y@example.org", "z@example.org"));
            await Db.CommitAsync();
        }

        var otherAdmin = isOwner ? await GetAdminAsync() : await CreateNonOwnerAdminAsync();
        var page = await CreatePageAsync();
        var result = await page.ViewAsync(otherAdmin);

        Assert.IsFalse(result.IsRequired);
        Assert.AreEqual(isOwner, result.IsInteractable);
    }

    [TestMethod]
    public async Task EditReturnsErrorWhenEmailFailsToSend()
    {
        var sender = new BrokenEmailSender();
        var page = await CreatePageAsync(sender);
        var result = await page.EditAsync(AdminEmailAddress, new(new Uri("smtp://example.com:587", UriKind.Absolute), "x", "x", "x", "y@example.org", "z@example.org"));

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditSendsEmail()
    {
        {
            var page = await CreatePageAsync();
            var result = await page.EditAsync(AdminEmailAddress, new(new Uri("smtp://example.com:587", UriKind.Absolute), "x", "x", "x", "y@example.org", "z@example.org"));
            await Db.CommitAsync();
            Assert.AreEqual(Status.ImportantInformation, result.Status);
        }

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(AdminEmailAddress, email.Recipient);
        Assert.AreEqual(Operation.CreatePageAction<Admin?, EmailSetupPage>(nameof(EmailSetupPage.VerifyEmailAddressAsync)), email.Operation);
    }

    [TestMethod]
    public async Task EditSetsEmailSettings()
    {
        {
            var page = await CreatePageAsync();
            var result = await page.EditAsync(AdminEmailAddress, new(new Uri("smtp://example.com:587", UriKind.Absolute), "a", "bb", "ccc", "y@example.org", "z@example.org"));
            await Db.CommitAsync();
            Assert.AreEqual(Status.ImportantInformation, result.Status);
        }

        var config = await Config.CreateAsync(Db);
        Assert.IsNotNull(config.EmailSenderSettings);
        Assert.AreEqual(new Uri("smtp://example.com:587", UriKind.Absolute), config.EmailSenderSettings.Uri);
        Assert.AreEqual("a", config.EmailSenderSettings.UserName);
        Assert.AreEqual("bb", config.EmailSenderSettings.Password);
        Assert.AreEqual("ccc", config.EmailSenderSettings.SenderName);
        Assert.AreEqual("y@example.org", config.EmailSenderSettings.SenderAddress);
        Assert.AreEqual("z@example.org", config.EmailSenderSettings.ReplyToAddress);
        Assert.AreEqual(config.EmailSenderSettings, EmailSender.LastSettings);
    }

    [TestMethod]
    public async Task EditIsIdempotent()
    {
        var settings = new EmailSenderSettings(new Uri("smtp://example.com:587", UriKind.Absolute), "a", "bb", "ccc", "y@example.org", "z@example.org");
        {
            var page = await CreatePageAsync();
            var result = await page.EditAsync(AdminEmailAddress, settings);
            await Db.CommitAsync();
            Assert.AreEqual(Status.ImportantInformation, result.Status);
        }

        {
            var page = await CreatePageAsync();
            var result = await page.EditAsync(AdminEmailAddress, settings);
            await Db.CommitAsync();
            Assert.AreEqual(Status.ImportantInformation, result.Status);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EditSetsAuthSecretAfterFirstSuccess(bool first)
    {
        var existingSecret = new AuthenticationSecret([0, 1, 2, 3]);
        if (!first)
        {
            var existingConfig = await Config.CreateAsync(Db);
            existingConfig.Set(existingSecret);
            await Db.CommitAsync();
        }

        {
            var page = await CreatePageAsync();
            var result = await page.EditAsync(AdminEmailAddress, new(new Uri("smtp://example.com:587", UriKind.Absolute), "a", "bb", "ccc", "y@example.org", "z@example.org"));
            await Db.CommitAsync();
            Assert.AreEqual(Status.ImportantInformation, result.Status);
        }

        var config = await Config.CreateAsync(Db);
        Assert.IsNotNull(config.AuthenticationSecret);

        if (first)
        {
            Assert.HasCount(Authenticator.DesiredKeySizeInBytes, config.AuthenticationSecret.HashKey);
            Assert.IsNotNull(EmailSender.LastSecret);
            Assert.AreSequenceEqual(config.AuthenticationSecret.HashKey, EmailSender.LastSecret.HashKey);
        }
        else
        {
            Assert.AreSequenceEqual(existingSecret.HashKey, config.AuthenticationSecret.HashKey);
            Assert.IsNull(EmailSender.LastSecret);
        }
    }

    [TestMethod]
    public async Task VerifyEmailAddressSetsVerifiedProperty()
    {
        {
            Db.Admins.Add(new Admin("new-admin@example.org") { IsOwner = true });
            await Db.CommitAsync();
        }

        var newAdmin = await Db.Admins.FindAsync("new-admin@example.org");
        Assert.IsNotNull(newAdmin);

        var page = await CreatePageAsync(DisabledEmailSender);
        var result = await page.VerifyEmailAddressAsync(newAdmin);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var newNewAdmin = await Db.Admins.FindAsync("new-admin@example.org");
        Assert.IsNotNull(newNewAdmin);
        Assert.IsTrue(newNewAdmin.IsEmailAddressVerified);
    }

    [TestMethod]
    public async Task VerifyEmailAddressDoesNotChangeExistingAdmin()
    {
        var notOwner = await CreateOtherOwnerAdminAsync();
        var page = await CreatePageAsync(DisabledEmailSender);
        var result = await page.VerifyEmailAddressAsync(notOwner);
        Assert.AreEqual(Status.None, result.Status);
        Db.EnsureNoChanges();
    }

    // The backup import feature is tested in E2E tests

    private async Task<EmailSetupPage> CreatePageAsync(EmailSender? emailSender = null)
    {
        var config = await Config.CreateAsync(Db);
        return new EmailSetupPage(Db, new ConfigValue<AuthenticationSecret>(config), new ConfigValue<EmailSenderSettings>(config), FileStorage, emailSender ?? EmailSender);
    }

    private sealed class BrokenEmailSender : EmailSender
    {
        public override Task SendAsync(IReadOnlyCollection<Email> emails, EmailSenderSettings? overrideSettings = null, AuthenticationSecret? overrideSecret = null)
            => throw new InvalidOperationException("Fake exception");

        public override Task SendCopyAsync(string subject, string body, IReadOnlyCollection<string> recipients, Operation? operation = null, string? operationDescription = null)
            => throw new InvalidOperationException("Fake exception");
    }
}