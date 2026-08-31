using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class ParticipantsPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task ModelIsSortedParticipants()
    {
        {
            Db.Participants.Add(
                new Participant("unknown@example.org"), // no name yet
                new Participant("alice@example.org") { GivenName = "Alice", FamilyName = "Apple" },
                new Participant("bob@example.org") { GivenName = "Bob", Status = ParticipantStatus.WithdrawnBeforeConfirmation },
                new Participant("carol@example.org") { GivenName = "Alice", FamilyName = "AAA", Status = ParticipantStatus.Accepted }
            );
            await Db.CommitAsync();
        }

        var modelAsObject = await new ParticipantsPage(Db.Participants, DisabledEmailSender).GetModelAsync(await GetAdminAsync());

        var model = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(modelAsObject);
        var expected = await Db.Participants.OrderBy(p => p.GivenName == null)
                                            .ThenBy(p => p.GivenName)
                                            .ThenBy(p => p.FamilyName)
                                            .ToCollectionAsync();
        Assert.AreSequenceEqual(expected, model);
    }

    [TestMethod]
    public async Task SummaryIncludesParticipantsCountByStatus()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org"),
                new Participant("bob@example.org") { Status = ParticipantStatus.WithdrawnAfterConfirmation },
                new Participant("carol@example.org") { Status = ParticipantStatus.Accepted },
                new Participant("daniel@example.org") { Status = ParticipantStatus.ProfileFilled }
            );
            await Db.CommitAsync();
        }

        var view = await new ParticipantsPage(Db.Participants, DisabledEmailSender).ViewAsync(await GetAdminAsync());

        Assert.HasCount(3, view.Summary);
        Assert.AreEqual("Withdrawn after confirmation", view.Summary[0].Label);
        Assert.AreEqual("1", view.Summary[0].Text);
        Assert.AreEqual("Created", view.Summary[1].Label);
        Assert.AreEqual("2", view.Summary[1].Text);
        Assert.AreEqual("Accepted, not confirmed yet", view.Summary[2].Label);
        Assert.AreEqual("1", view.Summary[2].Text);
    }

    [TestMethod]
    public async Task ChangeEmailAddressSendsEmailToMigrate()
    {
        {
            Db.Participants.Add(new Participant("alice@example.org"));
            await Db.CommitAsync();
        }

        var result = await new ParticipantsPage(Db.Participants, EmailSender).ChangeEmailAddressAsync("alice@example.org", "bob@example.org");
        await Db.CommitAsync();
        Assert.AreEqual(Status.Success, result.Status);

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual("bob@example.org", participant.FutureEmailAddress);

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", email.Recipient);
        Assert.AreEqual(Operation.CreatePageAction<Participant?, EmailPage>(nameof(EmailPage.ChangeEmailAddressAsync), ("oldEmailAddress", "alice@example.org")), email.Operation);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeEmailAddressDoesNothingIfNewIsSameAsOld(bool sameCase)
    {
        {
            Db.Participants.Add(new Participant("alice@example.org"));
            await Db.CommitAsync();
        }

        var newEmailAddress = sameCase ? "alice@example.org" : "aLiCe@Example.Org";
        var result = await new ParticipantsPage(Db.Participants, EmailSender).ChangeEmailAddressAsync("alice@example.org", newEmailAddress);
        Assert.AreEqual(Status.None, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task ChangeEmailAddressFailsForUnknownOldEmailAddress()
    {
        var result = await new ParticipantsPage(Db.Participants, DisabledEmailSender).ChangeEmailAddressAsync("alice@example.org", "bob@example.org");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task ChangeEmailAddressFailsForKnownNewEmailAddress()
    {
        {
            Db.Participants.Add(new Participant("alice@example.org"));
            Db.Participants.Add(new Participant("bob@example.org"));
            await Db.CommitAsync();
        }

        var result = await new ParticipantsPage(Db.Participants, DisabledEmailSender).ChangeEmailAddressAsync("alice@example.org", "bob@example.org");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("Existing")]
    public async Task SetRemarksSetsRemarks(string existing)
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org") { AdminRemarks = existing }
            );
            await Db.CommitAsync();
        }

        {
            var result = await new ParticipantsPage(Db.Participants, DisabledEmailSender).SetRemarksAsync("alice@example.org", "Hello");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual("Hello", participant.AdminRemarks);
    }

    [TestMethod]
    public async Task SetRemarksCanClearRemarks()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org") { AdminRemarks = "Remarkable" }
            );
            await Db.CommitAsync();
        }

        {
            var result = await new ParticipantsPage(Db.Participants, DisabledEmailSender).SetRemarksAsync("alice@example.org", null);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.IsNull(participant.AdminRemarks);
    }

    [TestMethod]
    public async Task SetRemarksFailsForUnknownEmailAddress()
    {
        var result = await new ParticipantsPage(Db.Participants, DisabledEmailSender).SetRemarksAsync("alice@example.org", "Hello");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task SetSoftRejectionSetsItWhenPresentAndClearsItWhenMissing(bool aliceIsSoftRejected, bool bobIsSoftRejected)
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org") { IsSoftRejected = aliceIsSoftRejected },
                new Participant("bob@example.org") { IsSoftRejected = bobIsSoftRejected }
            );
            await Db.CommitAsync();
        }

        {
            var result = await new ParticipantsPage(Db.Participants, DisabledEmailSender).SetSoftRejectionAsync(["alice@example.org"]);
            await Db.CommitAsync();
            Assert.AreEqual(Status.Success, result.Status);
        }

        var alice = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(alice);

        var bob = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(bob);

        Assert.IsTrue(alice.IsSoftRejected);
        Assert.IsFalse(bob.IsSoftRejected);
    }
}