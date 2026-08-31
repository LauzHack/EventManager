using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class AdminsPageTests : AdminTestsBase
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PageIsVisibleOnlyToOwner(bool owner)
    {
        var admin = owner ? await GetAdminAsync()
                          : await CreateNonOwnerAdminAsync();

        var page = new AdminsPage(Db.Admins, DisabledEmailSender);
        var view = await page.ViewAsync(admin);

        Assert.AreEqual(owner, view.IsInteractable);
        Assert.IsFalse(view.IsRequired);
    }

    [TestMethod]
    public async Task SummaryIsEmpty()
    {
        {
            Db.Admins.Add(
                new Admin("admin2@example.org")
            );
            await Db.CommitAsync();
        }

        var view = await new AdminsPage(Db.Admins, DisabledEmailSender).ViewAsync(await GetAdminAsync());

        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    public async Task ModelIsAdmins()
    {
        {
            Db.Admins.Add(
                new Admin("admin2@example.org")
            );
            await Db.CommitAsync();
        }

        var modelAsObject = await new AdminsPage(Db.Admins, DisabledEmailSender).GetModelAsync(await GetAdminAsync());

        var model = Assert.IsInstanceOfType<IReadOnlyCollection<Admin>>(modelAsObject);
        Assert.AreSequenceEqual(await Db.Admins.ToCollectionAsync(), model);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddCanAddNewAdmin(bool owner)
    {
        {
            var page = new AdminsPage(Db.Admins, EmailSender);
            var result = await page.AddAsync(await GetAdminAsync(), "admin2@example.org", owner);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newAdmin = await Db.Admins.FindAsync("admin2@example.org");
        Assert.IsNotNull(newAdmin);
        Assert.IsTrue(newAdmin.IsEmailAddressVerified);
        Assert.AreEqual(owner, newAdmin.IsOwner);
    }

    [TestMethod]
    public async Task AddSendsEmail()
    {
        {
            var result = await new AdminsPage(Db.Admins, EmailSender).AddAsync(await GetAdminAsync(), "admin2@example.org", false);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var single = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("admin2@example.org", single.Recipient);
    }

    [TestMethod]
    public async Task AddSendsEmailWhenAlreadyExists()
    {
        {
            Db.Admins.Add(new("admin2@example.org"));
            await Db.CommitAsync();
        }

        var result = await new AdminsPage(Db.Admins, EmailSender).AddAsync(await GetAdminAsync(), "admin2@example.org", false);

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("admin2@example.org", email.Recipient);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task AddCanGiveOwnershipRightsToExistingAdmin()
    {
        {
            Db.Admins.Add(new("admin2@example.org"));
            await Db.CommitAsync();
        }

        var page = new AdminsPage(Db.Admins, EmailSender);
        var result = await page.AddAsync(await GetAdminAsync(), "admin2@example.org", true);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var admin2 = await Db.Admins.FindAsync("admin2@example.org");
        Assert.IsNotNull(admin2);
        Assert.IsTrue(admin2.IsOwner);
    }

    [TestMethod]
    public async Task AddCanRemoveOwnershipRightsFromExistingAdmin()
    {
        {
            Db.Admins.Add(new("admin2@example.org") { IsOwner = true });
            await Db.CommitAsync();
        }

        var page = new AdminsPage(Db.Admins, EmailSender);
        var result = await page.AddAsync(await GetAdminAsync(), "admin2@example.org", false);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var admin2 = await Db.Admins.FindAsync("admin2@example.org");
        Assert.IsNotNull(admin2);
        Assert.IsFalse(admin2.IsOwner);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task AddCannotModifyOneself(bool sameCase, bool giveOwner)
    {
        var page = new AdminsPage(Db.Admins, EmailSender);
        var result = await page.AddAsync(await GetAdminAsync(), sameCase ? AdminEmailAddress : AdminEmailAddress.ToUpperInvariant(), giveOwner);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RemoveCanRemoveExistingAdmin()
    {
        {
            Db.Admins.Add(
                new Admin("admin2@example.org")
            );
            await Db.CommitAsync();
        }

        {
            var result = await new AdminsPage(Db.Admins, EmailSender).RemoveAsync(await GetAdminAsync(), "admin2@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        Assert.AreSequenceEqual([AdminEmailAddress], await Db.Admins.Select(a => a.EmailAddress).ToCollectionAsync());
    }

    [TestMethod]
    public async Task RemoveReturnsErrorWhenRemovingNonexistentAdmin()
    {
        var result = await new AdminsPage(Db.Admins, EmailSender).RemoveAsync(await GetAdminAsync(), "admin2@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RemoveCannotRemoveSelf()
    {
        var result = await new AdminsPage(Db.Admins, DisabledEmailSender).RemoveAsync(await GetAdminAsync(), AdminEmailAddress);

        Assert.AreEqual(Status.UserError, result.Status);
    }
}