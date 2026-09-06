using System;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class MassEmailPageTests : AdminTestsBase
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PageIsOptionalForOwnersAndHiddenFromOthers(bool isOwner)
    {
        var page = new MassEmailPage(Db.Participants, DisabledEmailSender);

        var admin = isOwner ? await GetAdminAsync()
                            : await CreateNonOwnerAdminAsync();
        var view = await page.ViewAsync(admin);

        Assert.IsFalse(view.IsRequired);
        Assert.AreEqual(isOwner, view.IsInteractable);
        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OnlyParticipantsWithTheSelectedStatusGetTheEmail(bool includeViewOp)
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org") { Status = ParticipantStatus.WithdrawnBeforeConfirmation },
                new Participant("bob@example.org") { Status = ParticipantStatus.Accepted },
                new Participant("carol@example.org") { Status = ParticipantStatus.Finalized },
                new Participant("daniel@example.org") { Status = ParticipantStatus.Confirmed }
            );
            await Db.CommitAsync();
        }

        var page = new MassEmailPage(Db.Participants, EmailSender);
        var result = await page.SendToParticipantsAsync(ParticipantStatus.Accepted, ParticipantStatus.Confirmed, "Hello", "World", includeViewOp);
        Assert.AreEqual(Status.Success, result.Status);

        Assert.HasCount(2, EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", EmailSender.Outbox[0].Recipient);
        Assert.AreEqual("daniel@example.org", EmailSender.Outbox[1].Recipient);
        Assert.AreEqual("Hello", EmailSender.Outbox[0].Subject);
        Assert.AreEqual("World", EmailSender.Outbox[0].Body);
        if (includeViewOp)
        {
            Assert.AreEqual(Operation.CreatePageView<Participant>(), EmailSender.Outbox[0].Operation);
        }
        else
        {
            Assert.IsNull(EmailSender.Outbox[0].Operation);
        }
    }

    [TestMethod]
    public async Task SendToParticipantsFailsWhenRangeIsInvalid()
    {
        var page = new MassEmailPage(Db.Participants, DisabledEmailSender);
        var result = await page.SendToParticipantsAsync(ParticipantStatus.Confirmed, ParticipantStatus.Created, "subject", "body", false);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SendToEmailAddressesWithoutApplicationLinkDoesSo()
    {
        var page = new MassEmailPage(Db.Participants, EmailSender);
        var result = await page.SendToEmailAddressesAsync(["john@example.org", "jane@example.org"], "Hello", "World", false, null);
        Assert.AreEqual(Status.Success, result.Status);
        // no "0 were dupes", that's useless
        Assert.DoesNotContain("0", result.Text, StringComparison.Ordinal);

        Assert.HasCount(2, EmailSender.Outbox);
        Assert.AreEqual("john@example.org", EmailSender.Outbox[0].Recipient);
        Assert.AreEqual("jane@example.org", EmailSender.Outbox[1].Recipient);
        Assert.AreEqual("Hello", EmailSender.Outbox[0].Subject);
        Assert.AreEqual("World", EmailSender.Outbox[0].Body);
        Assert.IsNull(EmailSender.Outbox[0].Operation);
    }

    [TestMethod]
    public async Task SendToEmailAddressesWithoutApplicationLinkDeduplicatesEmailAddresses()
    {
        var page = new MassEmailPage(Db.Participants, EmailSender);
        Assert.IsTrue(page.RedisplayAfterAction);
        var result = await page.SendToEmailAddressesAsync([
            "alice@example.org",
            "bob@example.org",
            "ALIce@Example.org",
            "Alice@example.org",
            "BOB@example.org"
        ], "Hello", "World", false, null);
        Assert.AreEqual(Status.Success, result.Status);
        Assert.Contains("2 emails", result.Text, StringComparison.Ordinal);
        Assert.Contains("(3 addresses were duplicates", result.Text, StringComparison.Ordinal);

        Assert.HasCount(2, EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", EmailSender.Outbox[0].Recipient);
        Assert.AreEqual("bob@example.org", EmailSender.Outbox[1].Recipient);
        Assert.AreEqual("Hello", EmailSender.Outbox[0].Subject);
        Assert.AreEqual("World", EmailSender.Outbox[0].Body);
        Assert.IsNull(EmailSender.Outbox[0].Operation);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("LonkedOn")]
    public async Task SendToEmailAddressesWithApplicationLinkDoesSo(string? referrer)
    {
        var page = new MassEmailPage(Db.Participants, EmailSender);
        var result = await page.SendToEmailAddressesAsync(["john@example.org", "jane@example.org"], "Hello", "World", true, referrer);
        Assert.AreEqual(Status.Success, result.Status);

        Assert.HasCount(2, EmailSender.Outbox);
        Assert.AreEqual("john@example.org", EmailSender.Outbox[0].Recipient);
        Assert.AreEqual("jane@example.org", EmailSender.Outbox[1].Recipient);
        Assert.AreEqual("Hello", EmailSender.Outbox[0].Subject);
        Assert.AreEqual("World", EmailSender.Outbox[0].Body);

        if (referrer is null)
        {
            Assert.AreEqual(Operation.CreatePageView<Participant>(), EmailSender.Outbox[0].Operation);
        }
        else
        {
            Assert.AreEqual(Operation.CreatePageView<Participant>().WithExtraTextArgument("utm_source", referrer), EmailSender.Outbox[0].Operation);
        }
        Assert.AreEqual("Apply", EmailSender.Outbox[0].OperationDescription);
    }

    [TestMethod]
    public async Task SendToEmailAddressesFailsWhenProvidingReferrerButNotRequestingApplicationLink()
    {
        var page = new MassEmailPage(Db.Participants, DisabledEmailSender);
        var result = await page.SendToEmailAddressesAsync(["john@example.org", "jane@example.org"], "subject", "body", false, "makes-no-sense");
        Assert.AreEqual(Status.UserError, result.Status);
    }
}