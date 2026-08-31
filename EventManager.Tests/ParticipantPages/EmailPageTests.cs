using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class EmailPageTests : ParticipantTestsBase
{
    [TestMethod]
    [DataRow(null)]
    [DataRow(ParticipantStatus.Created)]
    public async Task PageIsRequiredWithoutVerifiedParticipant(ParticipantStatus? status)
    {
        Participant? participant = status.HasValue ? new("example@example.org") { Status = status.Value } : null;
        var view = await GetPage(disableEmails: true).ViewAsync(participant);

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.EmailAddressVerified)]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task PageIsEditableWithVerifiedParticipant(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            await Db.CommitAsync();
        }

        var view = await GetPage(disableEmails: true).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EditWithNoParticipantSendsLoginEmail(bool hasReferrer)
    {
        {
            var result = await GetPage().EditAsync(null, "alice@example.org", hasReferrer ? "somewhere" : null);
            Assert.AreEqual(Status.ImportantInformation, result.Status);
            await Db.CommitAsync();
        }

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", email.Recipient);
        if (hasReferrer)
        {
            Assert.IsNotNull(email.Operation);
            Assert.IsTrue(email.Operation.Arguments.TryGetText("referrer", out var referrer));
            Assert.AreEqual("somewhere", referrer);
        }
        else
        {
            Assert.AreEqual(Operation.CreatePageAction<Participant?, EmailPage>(nameof(EmailPage.ConfirmEmailAddressAsync)), email.Operation);
        }
    }

    [TestMethod]
    public async Task EditWithNoParticipantIsIdempotent()
    {
        {
            var result = await GetPage().EditAsync(null, "alice@example.org", null);
            Assert.AreEqual(Status.ImportantInformation, result.Status);
            await Db.CommitAsync();
        }

        {
            var result = await GetPage().EditAsync(null, "alice@example.org", null);
            Assert.AreEqual(Status.ImportantInformation, result.Status);
            await Db.CommitAsync();
        }
    }

    [TestMethod]
    public async Task EditWithParticipantDoesNothingWhenSettingExistingAddress()
    {
        var result = await GetPage(disableEmails: true).EditAsync(await GetParticipantAsync(), ParticipantEmailAddress, null);

        Assert.AreEqual(Status.None, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task EditWithParticipantSendsEmailToChangeAddress()
    {
        var result = await GetPage().EditAsync(await GetParticipantAsync(), "bob@example.org", null);

        Assert.AreEqual(Status.ImportantInformation, result.Status);
        await Db.CommitAsync();

        var participant = await GetParticipantAsync();
        Assert.AreEqual("bob@example.org", participant.FutureEmailAddress);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", email.Recipient);
        Assert.AreEqual(Operation.CreatePageAction<Participant?, EmailPage>(nameof(EmailPage.ChangeEmailAddressAsync), ("oldEmailAddress", ParticipantEmailAddress)), email.Operation);
    }

    [TestMethod]
    public async Task EditWithParticipantIgnoresCaseOnlyEdits()
    {
        var result = await GetPage(disableEmails: true).EditAsync(await GetParticipantAsync(), ParticipantEmailAddress.ToUpperInvariant(), null);

        Assert.AreEqual(Status.None, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task EditWithParticipantReturnsErrorIfAddressInUse()
    {
        {
            var other = new Participant("bob@example.org");
            Db.Participants.Add(other);
            await Db.CommitAsync();
        }

        var result = await GetPage(disableEmails: true).EditAsync(await GetParticipantAsync(), "bob@example.org", null);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("somewhere")]
    public async Task ConfirmEmailAddressSetsStatusAndReferrer(string? referrer)
    {
        {
            var page = GetPage(disableEmails: true);
            var result = await page.ConfirmEmailAddressAsync(await GetParticipantAsync(), referrer);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await GetParticipantAsync();
        Assert.AreEqual(ParticipantStatus.EmailAddressVerified, participant.Status);
        Assert.AreEqual(referrer, participant.Referrer);
    }

    [TestMethod]
    public async Task ConfirmEmailAddressDoesNotChangeStatusIfAlreadyVerified()
    {
        {
            var oldParticipant = await GetParticipantAsync();
            oldParticipant.Status = ParticipantStatus.ProfileFilled;
            await Db.CommitAsync();
        }

        {
            var page = GetPage(disableEmails: true);
            var result = await page.ConfirmEmailAddressAsync(await GetParticipantAsync(), null);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await GetParticipantAsync();
        Assert.AreEqual(ParticipantStatus.ProfileFilled, participant.Status);
    }

    [TestMethod]
    public async Task ChangeEmailAddressDoesNothingIfOldEmailDoesNotExist()
    {
        var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "nonexistent@example.org");

        Assert.AreEqual(Status.None, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task ChangeEmailAddressMigratesData()
    {
        var originalDate = DateTimeOffset.UtcNow;
        {
            var oldParticipant = new Participant("alice@example.org")
            {
                FutureEmailAddress = ParticipantEmailAddress,
                Status = ParticipantStatus.ProfileFilled,
                GivenName = "Alice",
                FamilyName = "Smith",
                Profile = new Dictionary<string, string> { { "X", "Y" } }.ToImmutableDictionary(),
                IsSoftRejected = true,
                TravelReimbursementTier = "abc"
            };
            Db.Participants.Add(oldParticipant);
            Db.TravelExpenses.Add(new("111", originalDate, "Expense", 42.1m, "PLN", true) { Owners = { oldParticipant }, Status = TravelExpenseStatus.Reimbursed });
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        Assert.IsFalse(Db.Participants.Any(p => p.EmailAddress == "alice@example.org"));

        var participant = await GetParticipantAsync();
        Assert.IsNotNull(participant);
        Assert.AreEqual("Alice", participant.GivenName);
        Assert.AreEqual("Smith", participant.FamilyName);
        Assert.AreEqual("Y", participant.Profile["X"]);
        Assert.IsTrue(participant.IsSoftRejected);
        Assert.AreEqual("abc", participant.TravelReimbursementTier);
        var expense = Assert.ContainsSingle(Db.TravelExpenses);
        Assert.AreEqual("111", expense.ReceiptId);
        Assert.AreEqual(originalDate, expense.CreationDate);
        Assert.AreEqual("Expense", expense.Description);
        Assert.AreEqual(42.1m, expense.Amount);
        Assert.AreEqual("PLN", expense.CurrencyCode);
        Assert.AreEqual(TravelExpenseStatus.Reimbursed, expense.Status);
        Assert.IsTrue(expense.CountsDouble);
    }

    [TestMethod]
    public async Task ChangeEmailAddressTwiceMigratesData()
    {
        {
            var oldParticipant = new Participant("alice@example.org")
            {
                FutureEmailAddress = ParticipantEmailAddress,
                Status = ParticipantStatus.ProfileFilled,
                GivenName = "Alice",
                FamilyName = "Smith",
                Profile = new Dictionary<string, string> { { "X", "Y" } }.ToImmutableDictionary()
            };
            Db.Participants.Add(oldParticipant);
            Db.Participants.Add(new Participant("bob@example.org"));
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        {
            var participant2 = await GetParticipantAsync();
            participant2.FutureEmailAddress = "bob@example.org";
            await Db.CommitAsync();
        }

        {
            var other = await Db.Participants.FindAsync("bob@example.org");
            Assert.IsNotNull(other);
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(other, ParticipantEmailAddress);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        Assert.IsFalse(Db.Participants.Any(p => p.EmailAddress == ParticipantEmailAddress));

        var participant = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual("Alice", participant.GivenName);
        Assert.AreEqual("Smith", participant.FamilyName);
        Assert.AreEqual("Y", participant.Profile["X"]);
    }

    [TestMethod]
    public async Task ChangeEmailAddressMigratesApplicationGroupMembership()
    {
        {
            var oldParticipant = new Participant("alice@example.org")
            {
                FutureEmailAddress = ParticipantEmailAddress
            };
            var other = new Participant("bob@example.org") { Status = ParticipantStatus.ProfileFilled };
            Db.Participants.Add(
                oldParticipant,
                other
            );
            Db.ApplicationGroups.Add(new("id") { Members = { oldParticipant, other } });
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newOther));
        Assert.IsNotNull(newGroup);
        Assert.AreSequenceEqual([newParticipant, newOther], newGroup.Members, SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task ChangeEmailAddressMigratesApplicationGroupInvitations()
    {
        {
            var oldParticipant = new Participant("alice@example.org")
            {
                FutureEmailAddress = ParticipantEmailAddress
            };
            var other = new Participant("bob@example.org") { Status = ParticipantStatus.ProfileFilled };
            Db.Participants.Add(
                oldParticipant,
                other
            );
            Db.ApplicationGroups.Add(new("id") { Members = { other }, InvitedParticipants = { oldParticipant } });
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newOther));
        Assert.IsNotNull(newGroup);
        Assert.AreSequenceEqual([ParticipantEmailAddress], newGroup.InvitedParticipants.Select(p => p.EmailAddress));
    }

    [TestMethod]
    public async Task ChangeEmailAddressMigratesProjectMembership()
    {
        {
            var oldParticipant = new Participant("alice@example.org")
            {
                FutureEmailAddress = ParticipantEmailAddress
            };
            var other = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.CheckedIn,
            };
            Db.Participants.Add(
                oldParticipant,
                other
            );
            Db.Projects.Add(new Project("id", "Project", "Something", "Long", "https://example.org", "xyz") { Team = { other, oldParticipant } });
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newProject = Assert.ContainsSingle(Db.Projects);
        Assert.AreSequenceEqual([newParticipant, newOther], newProject.Team, SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task ChangeEmailAddressMigratesProjectInvitations()
    {
        {
            var oldParticipant = new Participant("alice@example.org")
            {
                FutureEmailAddress = ParticipantEmailAddress
            };
            var other = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.CheckedIn
            };
            Db.Participants.Add(
                oldParticipant,
                other
            );
            Db.Projects.Add(new Project("id", "Project", "Something", "Long", "https://example.org", "xyz")
            {
                Team = { other },
                InvitedParticipants = { oldParticipant }
            });
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newProject = Assert.ContainsSingle(Db.Projects);
        Assert.IsNotNull(newProject);
        Assert.AreSequenceEqual([ParticipantEmailAddress], newProject.InvitedParticipants.Select(e => e.EmailAddress));
    }

    [TestMethod]
    public async Task ChangeEmailAddressMigratesTravelExpenses()
    {
        {
            var oldParticipant = new Participant("alice@example.org")
            {
                FutureEmailAddress = ParticipantEmailAddress,
                Status = ParticipantStatus.CheckedIn
            };
            var other = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.Confirmed
            };
            Db.Participants.Add(oldParticipant, other);
            Db.TravelExpenses.Add(new("id", DateTimeOffset.UtcNow, "descr", 42.0m, "CHF", false) { Owners = { oldParticipant, other } });
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var expense = Assert.ContainsSingle(Db.TravelExpenses);
        var newParticipant = await GetParticipantAsync();
        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        Assert.AreSequenceEqual([newParticipant, newOther], expense.Owners, SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task ChangeEmailAddressMigratesOnlyInvitationsWhenOldEmailOwnerIsNotAliasChecked()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.ProfileFilled;
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            participant.Profile = new Dictionary<string, string> { { "X", "Y" } }.ToImmutableDictionary();

            var oldParticipant = new Participant("alice@example.org")
            {
                FutureEmailAddress = ParticipantEmailAddress
            };
            var other = new Participant("bob@example.org");
            Db.Participants.Add(
                oldParticipant,
                other
            );
            Db.ApplicationGroups.Add(new("id") { Members = { other }, InvitedParticipants = { oldParticipant } });
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual(ParticipantEmailAddress, newParticipant.EmailAddress);
        Assert.AreEqual(ParticipantStatus.ProfileFilled, newParticipant.Status);
        Assert.AreEqual("Alice Apple", newParticipant.FullName);
        Assert.AreEqual("Y", newParticipant.Profile["X"]);

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newOther));
        Assert.IsNotNull(newGroup);
        Assert.AreSequenceEqual([newParticipant.EmailAddress], newGroup.InvitedParticipants.Select(p => p.EmailAddress));

        Assert.IsNull(await Db.Participants.FindAsync("alice@example.org"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("other@example.org")]
    public async Task ChangeEmailAddressFailsIfFutureEmailAddressDoesNotMatch(string futureEmailAddress)
    {
        {
            Db.Participants.Add(new Participant("innocent@example.org") { FutureEmailAddress = futureEmailAddress });
            await Db.CommitAsync();
        }

        var result = await GetPage(disableEmails: true).ChangeEmailAddressAsync(await GetParticipantAsync(), "innocent@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SummaryIsEmailAddressOfParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.EmailAddressVerified;
            await Db.CommitAsync();
        }

        var view = await GetPage(disableEmails: true).ViewAsync(await GetParticipantAsync());

        Assert.AreSequenceEqual([("Address", ParticipantEmailAddress)], view.Summary);
    }

    private EmailPage GetPage(bool disableEmails = false)
        => new(Db.Participants, Db.ApplicationGroups, Db.Projects, Db.TravelExpenses, disableEmails ? DisabledEmailSender : EmailSender);
}