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
public sealed class TravelExpensesPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenWhenReimbursementIsNotEnabled()
    {
        var page = CreatePage(null);
        var result = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(result.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWhenReimbursementIsEnabled()
    {
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(result.IsRequired);
        Assert.IsTrue(result.IsInteractable);
    }

    [TestMethod]
    public async Task ModelIncludesOrderedExpensesWithContext()
    {
        var currency1 = new Currency("CHF", 1);
        var currency2 = new Currency("EUR", 1.2m);
        var participant1 = new Participant("alice@example.org")
        {
            GivenName = "Alice",
            FamilyName = "Apple",
            Status = ParticipantStatus.Confirmed,
            TravelReimbursementTier = "A"
        };
        var participant2 = new Participant("bob@example.org")
        {
            GivenName = "Bob",
            FamilyName = "Banana",
            Status = ParticipantStatus.Confirmed,
            TravelReimbursementTier = "B"
        };
        var expense0 = new TravelExpense("id0", DateTimeOffset.UtcNow.AddHours(1), "Descr 0", 3, "CHF", false) { Owners = { participant1 } };
        var expense1 = new TravelExpense("id1", DateTimeOffset.UtcNow, "Descr 1", 42, "CHF", false) { Status = TravelExpenseStatus.Approved, Owners = { participant1 } };
        var expense2 = new TravelExpense("id2", DateTimeOffset.UtcNow, "Descr 2", 10, "EUR", true) { CountsDouble = true, Owners = { participant1, participant2 } };
        Db.Currencies.Add(currency1, currency2);
        Db.TravelExpenses.Add(expense0, expense1, expense2);
        Db.Participants.Add(participant1, participant2);
        await Db.CommitAsync();

        var page = CreatePage(ReimbursementPolicy);
        var model = await page.GetModelAsync(await GetAdminAsync());

        var typed = Assert.IsInstanceOfType<IReadOnlyCollection<TravelExpensesPage.ExpenseWithContext>>(model);
        Assert.HasCount(3, typed);

        // sorted by status first then submission date with earliest first (thus expense0 must be after expense2)
        var actual2 = typed.ElementAt(0);
        Assert.AreEqual("id2", actual2.ReceiptId);
        Assert.AreEqual("Descr 2", actual2.Description);
        Assert.AreEqual(10m, actual2.Amount);
        Assert.AreEqual("EUR", actual2.CurrencyCode);
        Assert.IsTrue(actual2.CurrencyExchangeRate.HasValue);
        Assert.AreEqual(1.2m, actual2.CurrencyExchangeRate.Value);
        Assert.IsTrue(actual2.CountsDouble);
        Assert.HasCount(2, actual2.Owners);
        Assert.AreEqual("Alice Apple", actual2.Owners.ElementAt(0).FullName);
        Assert.AreEqual("A", actual2.Owners.ElementAt(0).ReimbursementTier);
        Assert.AreEqual("Bob Banana", actual2.Owners.ElementAt(1).FullName);
        Assert.AreEqual("B", actual2.Owners.ElementAt(1).ReimbursementTier);
        Assert.HasCount(2, actual2.OwnerExpenses);
        Assert.AreEqual(TravelExpenseStatus.Submitted, actual2.Status);
        // sorted here too, but approved first
        var sub21 = actual2.OwnerExpenses.ElementAt(0);
        Assert.AreEqual("Descr 1", sub21.Description);
        Assert.AreEqual(42m, sub21.Amount);
        Assert.AreEqual("CHF", sub21.CurrencyCode);
        Assert.IsFalse(sub21.CountsDouble);
        Assert.AreSequenceEqual(["Alice Apple"], sub21.OwnerNames);
        Assert.AreEqual(TravelExpenseStatus.Approved, sub21.Status);
        var sub20 = actual2.OwnerExpenses.ElementAt(1);
        Assert.AreEqual("Descr 0", sub20.Description);
        Assert.AreEqual(3m, sub20.Amount);
        Assert.AreEqual("CHF", sub20.CurrencyCode);
        Assert.IsFalse(sub20.CountsDouble);
        Assert.AreSequenceEqual(["Alice Apple"], sub20.OwnerNames);
        Assert.AreEqual(TravelExpenseStatus.Submitted, sub20.Status);

        var actual0 = typed.ElementAt(1);
        Assert.AreEqual("id0", actual0.ReceiptId);
        Assert.AreEqual("Descr 0", actual0.Description);
        Assert.AreEqual(3m, actual0.Amount);
        Assert.AreEqual("CHF", actual0.CurrencyCode);
        Assert.IsTrue(actual0.CurrencyExchangeRate.HasValue);
        Assert.AreEqual(1m, actual0.CurrencyExchangeRate.Value);
        Assert.IsFalse(actual0.CountsDouble);
        Assert.HasCount(1, actual0.Owners);
        Assert.HasCount(2, actual0.OwnerExpenses);
        Assert.AreEqual(TravelExpenseStatus.Submitted, actual0.Status);
        var sub01 = actual0.OwnerExpenses.ElementAt(0);
        Assert.AreEqual("Descr 1", sub01.Description);
        Assert.AreEqual(42m, sub01.Amount);
        Assert.AreEqual("CHF", sub01.CurrencyCode);
        Assert.IsFalse(sub01.CountsDouble);
        Assert.AreSequenceEqual(["Alice Apple"], sub01.OwnerNames);
        Assert.AreEqual(TravelExpenseStatus.Approved, sub01.Status);
        var sub02 = actual0.OwnerExpenses.ElementAt(1);
        Assert.AreEqual("Descr 2", sub02.Description);
        Assert.AreEqual(10m, sub02.Amount);
        Assert.AreEqual("EUR", sub02.CurrencyCode);
        Assert.IsTrue(sub02.CountsDouble);
        Assert.AreSequenceEqual(["Alice Apple", "Bob Banana"], sub02.OwnerNames);
        Assert.AreEqual(TravelExpenseStatus.Submitted, sub02.Status);

        var actual1 = typed.ElementAt(2);
        Assert.AreEqual("id1", actual1.ReceiptId);
        Assert.AreEqual("Descr 1", actual1.Description);
        Assert.AreEqual(42m, actual1.Amount);
        Assert.AreEqual("CHF", actual1.CurrencyCode);
        Assert.IsTrue(actual1.CurrencyExchangeRate.HasValue);
        Assert.AreEqual(1m, actual1.CurrencyExchangeRate.Value);
        Assert.IsFalse(actual1.CountsDouble);
        Assert.HasCount(1, actual1.Owners);
        Assert.HasCount(2, actual1.OwnerExpenses);
        Assert.AreEqual(TravelExpenseStatus.Approved, actual1.Status);
        // sorted by time ascending
        var sub12 = actual1.OwnerExpenses.ElementAt(0);
        Assert.AreEqual("Descr 2", sub12.Description);
        Assert.AreEqual(10m, sub12.Amount);
        Assert.AreEqual("EUR", sub12.CurrencyCode);
        Assert.IsTrue(sub12.CountsDouble);
        Assert.AreSequenceEqual(["Alice Apple", "Bob Banana"], sub12.OwnerNames);
        Assert.AreEqual(TravelExpenseStatus.Submitted, sub12.Status);
        var sub10 = actual1.OwnerExpenses.ElementAt(1);
        Assert.AreEqual("Descr 0", sub10.Description);
        Assert.AreEqual(3m, sub10.Amount);
        Assert.AreEqual("CHF", sub10.CurrencyCode);
        Assert.IsFalse(sub10.CountsDouble);
        Assert.AreSequenceEqual(["Alice Apple"], sub10.OwnerNames);
        Assert.AreEqual(TravelExpenseStatus.Submitted, sub10.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ApproveSendsEmailSetsApprovedFlagAndCurrencyExchangeRate(bool currencyExists)
    {
        {
            if (currencyExists)
            {
                Db.Currencies.Add(new Currency("EUR", 0.456m));
            }
            var participant = new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed };
            Db.Participants.Add(participant);
            Db.TravelExpenses.Add(new TravelExpense("id", DateTimeOffset.UtcNow, "ExpenseDescr", 10, "EUR", true) { Owners = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy, enableEmails: true);
        var result = await page.ApproveAsync("id", 1.2m);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", email.Recipient);
        Assert.Contains("approved", email.Body, StringComparison.Ordinal);
        Assert.Contains("ExpenseDescr", email.Body, StringComparison.Ordinal);

        var expense = await Db.TravelExpenses.FindAsync("id");
        Assert.IsNotNull(expense);
        Assert.AreEqual(TravelExpenseStatus.Approved, expense.Status);

        var currency = await Db.Currencies.FindAsync("EUR");
        Assert.IsNotNull(currency);
        Assert.AreEqual(1.2m, currency.ExchangeRate);
    }

    [TestMethod]
    [DataRow(TravelExpenseStatus.Approved)]
    [DataRow(TravelExpenseStatus.Reimbursed)]
    public async Task ApproveDoesNotSendEmailSetsIfAlreadyApproved(TravelExpenseStatus status)
    {
        {
            Db.Currencies.Add(new Currency("EUR", 0.456m));
            var participant = new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed };
            Db.Participants.Add(participant);
            Db.TravelExpenses.Add(new TravelExpense("id", DateTimeOffset.UtcNow, "ExpenseDescr", 10, "EUR", true) { Status = status, Owners = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy);
        var result = await page.ApproveAsync("id", 1.2m);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var expense = await Db.TravelExpenses.FindAsync("id");
        Assert.IsNotNull(expense);
        Assert.AreEqual(status, expense.Status);

        var currency = await Db.Currencies.FindAsync("EUR");
        Assert.IsNotNull(currency);
        Assert.AreEqual(1.2m, currency.ExchangeRate);
    }

    [TestMethod]
    public async Task ApproveFailsWhenExpenseIdIsUnknown()
    {
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.ApproveAsync("id", 1.2m);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task ApproveFailsWhenExchangeRateIsNotGreaterThanZero(int rate)
    {
        {
            var participant = new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed };
            Db.Participants.Add(participant);
            Db.TravelExpenses.Add(new TravelExpense("id", DateTimeOffset.UtcNow, "ExpenseDescr", 10, "EUR", true) { Owners = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy);
        var result = await page.ApproveAsync("id", rate);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ApproveWithChangesSetsExpensePropertiesAndSendsEmail(bool currencyExists)
    {
        {
            if (currencyExists)
            {
                Db.Currencies.Add(new Currency("EUR", 0.456m));
            }
            var participant = new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed };
            Db.Participants.Add(participant);
            Db.TravelExpenses.Add(new TravelExpense("id", DateTimeOffset.UtcNow, "ExpenseDescr", 10, "EUR", true) { Owners = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy, enableEmails: true);
        var result = await page.ApproveWithChangesAsync("id", 22.2m, "GBP", 1.5m, false, "this is a comment");
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", email.Recipient);
        Assert.Contains("approved with the following changes", email.Body, StringComparison.Ordinal);
        Assert.Contains("ExpenseDescr", email.Body, StringComparison.Ordinal);
        Assert.Contains("this is a comment", email.Body, StringComparison.Ordinal);
        Assert.Contains("EUR 10.0 -> GBP 22.2", email.Body, StringComparison.Ordinal);
        Assert.Contains("counts double -> does not count double", email.Body, StringComparison.Ordinal);

        var expense = await Db.TravelExpenses.FindAsync("id");
        Assert.IsNotNull(expense);
        Assert.AreEqual(22.2m, expense.Amount);
        Assert.AreEqual("GBP", expense.CurrencyCode);
        Assert.IsFalse(expense.CountsDouble);
        Assert.AreEqual(TravelExpenseStatus.Approved, expense.Status);

        var currency = await Db.Currencies.FindAsync("GBP");
        Assert.IsNotNull(currency);
        Assert.AreEqual(1.5m, currency.ExchangeRate);
    }

    [TestMethod]
    public async Task ApproveWithChangesFailsWhenExpenseIdIsUnknown()
    {
        var page = CreatePage(ReimbursementPolicy, enableEmails: true);
        var result = await page.ApproveWithChangesAsync("id", 10m, "CHF", 1m, false, "comment");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task ApproveWithChangesFailsWhenAmountIsNotGreaterThanZero(int amount)
    {
        {
            var participant = new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed };
            Db.Participants.Add(participant);
            Db.TravelExpenses.Add(new TravelExpense("id", DateTimeOffset.UtcNow, "ExpenseDescr", 10, "EUR", true) { Owners = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy, enableEmails: true);
        var result = await page.ApproveWithChangesAsync("id", amount, "EUR", 1.2m, true, "comment");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task ApproveWithChangesFailsWhenExchangeRateIsNotGreaterThanZero(int rate)
    {
        {
            var participant = new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed };
            Db.Participants.Add(participant);
            Db.TravelExpenses.Add(new TravelExpense("id", DateTimeOffset.UtcNow, "ExpenseDescr", 10, "EUR", true) { Owners = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy, enableEmails: true);
        var result = await page.ApproveWithChangesAsync("id", 10m, "EUR", rate, true, "comment");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    // this likely indicates the admin forgot to do something; silently approving without changes would not be good
    [TestMethod]
    public async Task ApproveWithChangesFailsWhenNoChangesAreMade()
    {
        {
            var participant = new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed };
            Db.Participants.Add(participant);
            Db.TravelExpenses.Add(new TravelExpense("id", DateTimeOffset.UtcNow, "ExpenseDescr", 10, "EUR", true) { Owners = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy, enableEmails: true);
        var result = await page.ApproveWithChangesAsync("id", 10, "EUR", 1.2m, true, "comment");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(TravelExpenseStatus.Submitted)]
    [DataRow(TravelExpenseStatus.Approved)]
    [DataRow(TravelExpenseStatus.Reimbursed)]
    public async Task RejectSendsEmailIfNotReimbursedAndDeletesExpense(TravelExpenseStatus status)
    {
        {
            Db.Currencies.Add(new Currency("EUR", 1.2m));
            var participant = new Participant("alice@example.org") { Status = ParticipantStatus.Confirmed };
            Db.Participants.Add(participant);
            Db.TravelExpenses.Add(new TravelExpense("id", DateTimeOffset.UtcNow, "ExpenseDescr", 10, "EUR", true) { Status = status, Owners = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy, enableEmails: true);
        var result = await page.RejectAsync("id", "some reason");
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        if (status is TravelExpenseStatus.Reimbursed)
        {
            Assert.IsEmpty(EmailSender.Outbox);
        }
        else
        {
            var email = Assert.ContainsSingle(EmailSender.Outbox);
            Assert.AreEqual("alice@example.org", email.Recipient);
            Assert.Contains("rejected", email.Body, StringComparison.Ordinal);
            Assert.Contains("ExpenseDescr", email.Body, StringComparison.Ordinal);
            Assert.Contains("some reason", email.Body, StringComparison.Ordinal);
        }

        var expense = await Db.TravelExpenses.FindAsync("id");
        Assert.IsNull(expense);
    }

    [TestMethod]
    public async Task RejectFailsWhenExpenseIdIsUnknown()
    {
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.RejectAsync("id", "some reason");
        Assert.AreEqual(Status.UserError, result.Status);
        await Db.CommitAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CreateDoesSo(bool alreadyReimbursed)
    {
        {
            Db.Participants.Add(new("alice@example.org") { Status = ParticipantStatus.Confirmed });
            Db.Participants.Add(new("bob@example.org") { Status = ParticipantStatus.Confirmed });
            await Db.CommitAsync();
        }

        var page = CreatePage(ReimbursementPolicy, enableTime: true);
        var receipt = new File.InMemory("receipt", "application/pdf", [0]);
        var result = await page.CreateAsync(receipt, "descr", 42m, "CHF", 1.1m, ["alice@example.org", "bob@example.org"], alreadyReimbursed);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var expenses = await Db.TravelExpenses.ToCollectionAsync();
        Assert.HasCount(2, expenses);
        foreach (var expense in expenses)
        {
            Assert.AreEqual("descr", expense.Description);
            Assert.AreEqual(TimeProvider.GetUtcNow(), expense.CreationDate);
            // important to check the per-person amount here!
            Assert.AreEqual(42m, expense.AmountToReimbursePerPerson);
            Assert.AreEqual("CHF", expense.CurrencyCode);

            var storedReceipt = await FileStorage.GetFileAsync(expense.ReceiptId);
            Assert.IsNotNull(storedReceipt);
            Assert.AreEqual(receipt.Name, storedReceipt.Name);

            var currency = Assert.ContainsSingle(await Db.Currencies.ToCollectionAsync());
            Assert.AreEqual("CHF", currency.Code);
            Assert.AreEqual(1.1m, currency.ExchangeRate);

            Assert.AreEqual(alreadyReimbursed ? TravelExpenseStatus.Reimbursed : TravelExpenseStatus.Approved, expense.Status);
        }

        expenses = [.. expenses.OrderBy(e => e.Owners.First().EmailAddress)];
        Assert.AreEqual("alice@example.org", Assert.ContainsSingle(expenses.First().Owners).EmailAddress);
        Assert.AreEqual("bob@example.org", Assert.ContainsSingle(expenses.Last().Owners).EmailAddress);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task CreateFailsWhenAmountIsNotGreaterThanZero(int amount)
    {
        {
            Db.Participants.Add(new("alice@example.org") { Status = ParticipantStatus.Confirmed });
            await Db.CommitAsync();
        }
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.CreateAsync(new File.InMemory("receipt", "application/pdf", [0]), "descr", amount, "CHF", 1m, ["alice@example.org"], false);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task CreateFailsWhenExchangeRateIsNotGreaterThanZero(int rate)
    {
        {
            Db.Participants.Add(new("alice@example.org") { Status = ParticipantStatus.Confirmed });
            await Db.CommitAsync();
        }
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.CreateAsync(new File.InMemory("receipt", "application/pdf", [0]), "descr", 10m, "CHF", rate, ["alice@example.org"], false);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CreateFailsWhenEmailAddressesIsEmpty()
    {
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.CreateAsync(new File.InMemory("receipt", "application/pdf", [0]), "descr", 10m, "CHF", 1m, [], false);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CreateFailsWhenEmailAddressesContainsUnknownAddresses()
    {
        {
            Db.Participants.Add(new("alice@example.org") { Status = ParticipantStatus.Confirmed });
            await Db.CommitAsync();
        }
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.CreateAsync(new File.InMemory("receipt", "application/pdf", [0]), "descr", 10m, "CHF", 1m, ["alice@example.org", "unknown@example.org"], false);
        Assert.AreEqual(Status.UserError, result.Status);
        Assert.Contains("Unknown email addresses: unknown@example.org", result.Text, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CreateFailsWhenEmailAddressesContainsAddressesOfNotConfirmedParticipants()
    {
        {
            Db.Participants.Add(new("alice@example.org") { Status = ParticipantStatus.Confirmed });
            Db.Participants.Add(new("bob@example.org") { Status = ParticipantStatus.Finalized });
            await Db.CommitAsync();
        }
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.CreateAsync(new File.InMemory("receipt", "application/pdf", [0]), "descr", 10m, "CHF", 1m, ["alice@example.org", "bob@example.org"], false);
        Assert.AreEqual(Status.UserError, result.Status);
        Assert.Contains("Non-confirmed participants: bob@example.org", result.Text, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CreateFailsWhenEmailAddressesContainsUnknownAddressesAndThoseOfNotConfirmedParticipants()
    {
        {
            Db.Participants.Add(new("alice@example.org") { Status = ParticipantStatus.Confirmed });
            Db.Participants.Add(new("bob@example.org") { Status = ParticipantStatus.Finalized });
            await Db.CommitAsync();
        }
        var page = CreatePage(ReimbursementPolicy);
        var result = await page.CreateAsync(new File.InMemory("receipt", "application/pdf", [0]), "descr", 10m, "CHF", 1m, ["alice@example.org", "bob@example.org", "unknown@example.org"], false);
        Assert.AreEqual(Status.UserError, result.Status);
        Assert.Contains("Unknown email addresses: unknown@example.org", result.Text, StringComparison.Ordinal);
        Assert.Contains("Non-confirmed participants: bob@example.org", result.Text, StringComparison.Ordinal);
    }

    private TravelExpensesPage CreatePage(TravelReimbursementPolicy? policy, bool enableEmails = false, bool enableTime = false)
        => new(
               Db.Participants, Db.TravelExpenses, Db.Currencies,
               policy, FileStorage,
               enableEmails ? EmailSender : DisabledEmailSender,
               enableTime ? TimeProvider : DisabledTimeProvider
           );
}