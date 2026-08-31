using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

using EventManager.AdminPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class TravelReimbursementPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenWhenReimbursementIsNotEnabled()
    {
        var page = new TravelReimbursementPage(Db.TravelExpenses, Db.Currencies, null);
        var result = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(result.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWhenReimbursementIsEnabled()
    {
        var page = new TravelReimbursementPage(Db.TravelExpenses, Db.Currencies, ReimbursementPolicy);
        var result = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(result.IsRequired);
        Assert.IsTrue(result.IsInteractable);
    }

    [TestMethod]
    public async Task ModelIncludesCorrectAmounts()
    {
        {
            // Deliberately not in alphabetical order to test the sorting
            var participant1 = new Participant("bob@example.org") { GivenName = "Bob", FamilyName = "Banana", TravelReimbursementTier = "A", Status = ParticipantStatus.Demoed };
            var participant2 = new Participant("alice@example.org") { GivenName = "Alice", FamilyName = "Apple", TravelReimbursementTier = "A", Status = ParticipantStatus.CheckedIn };
            var participant3 = new Participant("carol@example.org") { GivenName = "Carol", FamilyName = "Coconut", TravelReimbursementTier = "A", Status = ParticipantStatus.Confirmed };

            var currency1 = new Currency("CHF", 1);
            var currency2 = new Currency("EUR", 1.2m);

            // Normal expense, single person
            var expense0 = new TravelExpense("id0", DateTimeOffset.UtcNow, "Expense 0", 10, "CHF", false) { Status = TravelExpenseStatus.Approved, Owners = { participant1 } };
            // Normal expense, single person, different casing
            var expense1 = new TravelExpense("id1", DateTimeOffset.UtcNow, "Expense 1", 10, "chf", false) { Status = TravelExpenseStatus.Approved, Owners = { participant1 } };
            // Counts-double expense, single person
            var expense2 = new TravelExpense("id2", DateTimeOffset.UtcNow, "Expense 2", 2, "CHF", true) { Status = TravelExpenseStatus.Approved, Owners = { participant1 } };
            // Expense shared among participants
            var expense3 = new TravelExpense("id3", DateTimeOffset.UtcNow, "Expense 3", 5, "CHF", false) { Status = TravelExpenseStatus.Approved, Owners = { participant1, participant2 } };
            // Normal expense, but for a different currency
            var expense4 = new TravelExpense("id4", DateTimeOffset.UtcNow, "Expense 4", 30, "EUR", false) { Status = TravelExpenseStatus.Approved, Owners = { participant2 } };
            // Everything all at once
            var expense5 = new TravelExpense("id5", DateTimeOffset.UtcNow, "Expense 5", 47, "EUR", true) { Status = TravelExpenseStatus.Approved, Owners = { participant2, participant3 } };
            // Normal expense, but for a participant not checked in
            var expense6 = new TravelExpense("id6", DateTimeOffset.UtcNow, "Expense 6", 20, "CHF", false) { Status = TravelExpenseStatus.Approved, Owners = { participant3 } };
            // Expense not approved
            var expense7 = new TravelExpense("id7", DateTimeOffset.UtcNow, "Expense 7", 666, "CHF", false) { Owners = { participant1 } };
            // Pre-reimbursed expense
            var expense8 = new TravelExpense("id8", DateTimeOffset.UtcNow, "Expense 8", 2, "CHF", false) { Status = TravelExpenseStatus.Reimbursed, Owners = { participant1 } };

            Db.Participants.Add(participant1, participant2, participant3);
            Db.Currencies.Add(currency1, currency2);
            Db.TravelExpenses.Add(expense0, expense1, expense2, expense3, expense4, expense5, expense6, expense7, expense8);
            await Db.CommitAsync();
        }

        var page = new TravelReimbursementPage(Db.TravelExpenses, Db.Currencies, ReimbursementPolicy);
        var model = await page.GetModelAsync(await GetAdminAsync());

        var reimbursements = Assert.IsInstanceOfType<IReadOnlyCollection<TravelReimbursementPage.ParticipantReimbursement>>(model);
        Assert.AreSequenceEqual([
            // (5 / 2) + (30 * 1.2) + (47 * 1.2 / 2 * 2) is 94.9, rounded to upper 5 -> 95
            new("Alice Apple", "alice@example.org", false, 95m, 999m, null),
            // 10 + 10 + (2 * 2) + (5 / 2) - 2 is 24.5, rounded to upper 5 -> 25
            new("Bob Banana", "bob@example.org", true, 25m, 999m, null)
            // not the 3rd participant, who isn't checked in
        ], reimbursements);
    }

    [TestMethod]
    public async Task ModelIncludesTierCaps()
    {
        {
            var participant1 = new Participant("alice@example.org")
            {
                GivenName = "Alice",
                FamilyName = "Apple",
                TravelReimbursementTier = "A",
                Status = ParticipantStatus.CheckedIn
            };
            var participant2 = new Participant("bob@example.org")
            {
                GivenName = "Bob",
                FamilyName = "Banana",
                TravelReimbursementTier = "B",
                Status = ParticipantStatus.Demoed,
                AdminRemarks = "This one's special"
            };
            var participant3 = new Participant("carol@example.org")
            {
                GivenName = "Carol",
                FamilyName = "Coconut",
                TravelReimbursementTier = "C",
                Status = ParticipantStatus.Demoed
            };

            var currency1 = new Currency("CHF", 1);
            var currency2 = new Currency("EUR", 1.2m);

            var expense1 = new TravelExpense("id1", DateTimeOffset.UtcNow, "Expense 1", 4, "CHF", true) { Status = TravelExpenseStatus.Approved, Owners = { participant1 } };
            var expense2 = new TravelExpense("id2", DateTimeOffset.UtcNow, "Expense 2", 2, "CHF", true) { Status = TravelExpenseStatus.Approved, Owners = { participant2 } };
            var expense3 = new TravelExpense("id3", DateTimeOffset.UtcNow, "Expense 3", 20, "EUR", false) { Status = TravelExpenseStatus.Approved, Owners = { participant1, participant2 } };
            var expense4 = new TravelExpense("id4", DateTimeOffset.UtcNow, "Expense 4", 2, "CHF", false) { Status = TravelExpenseStatus.Reimbursed, Owners = { participant2 } };

            Db.Participants.Add(participant1, participant2, participant3);
            Db.Currencies.Add(currency1, currency2);
            Db.TravelExpenses.Add(expense1, expense2, expense3, expense4);
            await Db.CommitAsync();
        }

        var policy = new TravelReimbursementPolicy("CHF", "Descr", new("https://example.org"), ImmutableDictionary.CreateRange<string, decimal>([new("A", 10), new("B", 20)]), 3);
        var page = new TravelReimbursementPage(Db.TravelExpenses, Db.Currencies, policy);
        var model = await page.GetModelAsync(await GetAdminAsync());

        var reimbursements = Assert.IsInstanceOfType<IReadOnlyCollection<TravelReimbursementPage.ParticipantReimbursement>>(model);
        Assert.AreSequenceEqual([
            // 10 rounded to upper 3 -> 12
            new("Alice Apple", "alice@example.org", false, 12m, 10m, null),
            // (20 / 2 * 1.2) + (2 * 2) - 2 is 14, rounded to upper 3 -> 15
            new("Bob Banana", "bob@example.org", true, 15m, 20m, "This one's special")
            // Carol has no expenses and is thus not shown
        ], reimbursements);
    }

    // We've had crashes in prod due to another page having a bug and not enforcing system invariants before...
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task ModelDoesNotCrashEvenIfInvariantsAreUnexpectedlyBroken(int mode)
    {
        {
            var participant1 = new Participant("bob@example.org") { GivenName = "Bob", FamilyName = "Banana", TravelReimbursementTier = "A", Status = ParticipantStatus.Demoed };
            if (mode == 0)
            {
                participant1.GivenName = null;
                participant1.FamilyName = null;
            }
            if (mode == 1)
            {
                participant1.TravelReimbursementTier = null;
            }
            else if (mode == 2)
            {
                participant1.TravelReimbursementTier = "does not exist";
            }
            var currency1 = new Currency("CHF", 1);
            var expense0 = new TravelExpense("id0", DateTimeOffset.UtcNow, "Expense 0", 10, "CHF", false) { Status = TravelExpenseStatus.Approved, Owners = { participant1 } };

            Db.Participants.Add(participant1);
            Db.Currencies.Add(currency1);
            Db.TravelExpenses.Add(expense0);
            await Db.CommitAsync();
        }

        var page = new TravelReimbursementPage(Db.TravelExpenses, Db.Currencies, mode == 3 ? null : ReimbursementPolicy);
        var model = await page.GetModelAsync(await GetAdminAsync());

        var reimbursements = Assert.IsInstanceOfType<IReadOnlyCollection<TravelReimbursementPage.ParticipantReimbursement>>(model);
        if (mode != 3)
        {
            Assert.HasCount(1, reimbursements);
        }
    }

    // Nothing we can do in this case but throw, since "hiding" the expense would be a terrible idea
    [TestMethod]
    public async Task ModelCrashesIfExpenseIsUnexpectedlyInUnknownCurrency()
    {
        {
            var participant1 = new Participant("bob@example.org") { GivenName = "Bob", FamilyName = "Banana", TravelReimbursementTier = "A", Status = ParticipantStatus.Demoed };
            var expense0 = new TravelExpense("id0", DateTimeOffset.UtcNow, "Expense 0", 10, "CHF", false) { Status = TravelExpenseStatus.Approved, Owners = { participant1 } };

            Db.Participants.Add(participant1);
            // no currency!
            Db.TravelExpenses.Add(expense0);
            await Db.CommitAsync();
        }

        var page = new TravelReimbursementPage(Db.TravelExpenses, Db.Currencies, ReimbursementPolicy);
        var admin = await GetAdminAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.GetModelAsync(admin));
    }
}