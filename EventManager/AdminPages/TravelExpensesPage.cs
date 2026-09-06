using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

namespace EventManager.AdminPages;

public sealed class TravelExpensesPage(DbValues<Participant> participants, DbValues<TravelExpense> expenses, DbValues<Currency> currencies,
                                       TravelReimbursementPolicy? policy,
                                       FileStorage fileStorage, EmailSender emailSender, TimeProvider timeProvider) : Page<Admin>
{
    public override bool RedisplayAfterAction
        => true;

    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (policy is null)
        {
            return ForbiddenView();
        }
        var submittedExpensesCount = await expenses.CountAsync(e => e.Status == TravelExpenseStatus.Submitted);
        return EditableView("Travel expenses", "Handle", ("Requests pending approval", submittedExpensesCount));
    }

    public override async Task<object?> GetModelAsync(Admin admin)
    {
        var allExpenses = await expenses.ToCollectionAsync();
        var allCurrencies = await currencies.ToDictionaryAsync(c => c.Code, StringComparer.OrdinalIgnoreCase);
        return allExpenses.OrderBy(e => e.Status)
                          .ThenBy(e => e.CreationDate)
                          .Select(e => new ExpenseWithContext(
                              e.ReceiptId,
                              e.Description,
                              e.Amount,
                              e.CurrencyCode,
                              allCurrencies.TryGetValue(e.CurrencyCode, out var currency) ? currency.ExchangeRate : null,
                              e.CountsDouble,
                              [.. e.Owners.Select(o => new OwnerInfo(o.FullName, o.TravelReimbursementTier))],
                              [.. allExpenses.Where(e2 => e2 != e && e2.Owners.Intersect(e.Owners).Any())
                                             .OrderByDescending(e2 => e2.Status)
                                             .ThenBy(e2 => e2.CreationDate)
                                             .Select(e2 => new ExpenseInfo(
                                                               e2.Description,
                                                               e2.Amount,
                                                               e2.CurrencyCode,
                                                               e2.CountsDouble,
                                                               [.. e2.Owners.Select(o2 => o2.FullName)],
                                                               e2.Status
                                             ))],
                              e.Status
                          ))
                          .ToArray();
    }

    public async Task<StatusMessage> ApproveAsync(string receiptId, decimal currencyExchangeRate)
    {
        if (currencyExchangeRate <= 0)
        {
            return Error("Currency exchange rates must be greater than zero!");
        }

        var expense = await expenses.FindAsync(receiptId);
        if (expense is null)
        {
            return Error("Unknown expense");
        }

        await UpdateCurrencyAsync(expense.CurrencyCode, currencyExchangeRate);

        if (expense.Status is TravelExpenseStatus.Approved or TravelExpenseStatus.Reimbursed)
        {
            return Success("The expense was already approved. The exchange rate has been updated.");
        }

        expense.Status = TravelExpenseStatus.Approved;
        await emailSender.SendAsync([.. expense.Owners.Select(o => new Email(
            Recipient: o.EmailAddress,
            Subject: "Approved expense",
            Body: $"Your expense '{expense.Description}' has been approved. No action is required on your end.",
            Operation: null
        ))]);
        return Success("Expense approved. The expense owners were notified by email.");
    }

    public async Task<StatusMessage> ApproveWithChangesAsync(string receiptId, decimal amount, string currencyCode, decimal currencyExchangeRate, bool countsDouble, string comment)
    {
        if (amount <= 0)
        {
            return Error("Amounts must be greater than 0!");
        }
        if (currencyExchangeRate <= 0)
        {
            return Error("Currency exchange rates must be greater than zero!");
        }

        var expense = await expenses.FindAsync(receiptId);
        if (expense is null)
        {
            return Error("Unknown expense");
        }

        List<(string Old, string New)> changes = [];
        if (amount != expense.Amount || !currencyCode.Equals(expense.CurrencyCode, StringComparison.Ordinal))
        {
            changes.Add(($"{expense.CurrencyCode} {expense.Amount.ToString(CultureInfo.InvariantCulture)}", $"{currencyCode} {amount.ToString(CultureInfo.InvariantCulture)}"));
            expense.Amount = amount;
            expense.CurrencyCode = currencyCode;
        }
        if (countsDouble != expense.CountsDouble)
        {
            static string CountsDoubleToString(bool countsDouble) => countsDouble ? "counts double" : "does not count double";
            changes.Add((CountsDoubleToString(expense.CountsDouble), CountsDoubleToString(countsDouble)));
            expense.CountsDouble = countsDouble;
        }
        if (changes.Count == 0)
        {
            return Error("You did not make any changes.");
        }

        await UpdateCurrencyAsync(currencyCode, currencyExchangeRate);

        expense.Status = TravelExpenseStatus.Approved;
        await emailSender.SendAsync([.. expense.Owners.Select(o => new Email(
            Recipient: o.EmailAddress,
            Subject: "Approved expense",
            Body: $"Your expense '{expense.Description}' has been approved with the following changes:\n" +
                  string.Join('\n', changes.Select(c=>$"- {c.Old} -> {c.New}")) + "\n\n" +
                  comment + "\n\n" +
                  "No action is required on your end.",
            Operation: null
        ))]);
        return Success("Expense approved with changes. The expense owners were notified by email.");
    }

    public async Task<StatusMessage> RejectAsync(string receiptId, string reason)
    {
        var expense = await expenses.FindAsync(receiptId);
        if (expense is null)
        {
            return Error("Unknown expense");
        }

        expenses.Remove(expense);

        if (expense.Status is TravelExpenseStatus.Reimbursed)
        {
            return Success("Expense rejected. Since this was an already-reimbursed expense, the expense owners were not notified.");
        }

        await emailSender.SendAsync([.. expense.Owners.Select(o => new Email(
            Recipient: o.EmailAddress,
            Subject: "Rejected expense",
            Body: $"Your expense '{expense.Description}' has been rejected: {reason}.\n\nIf necessary, please re-submit an expense using the link below.",
            Operation: Operation.CreatePageView<Participant, TravelPage>(),
            OperationDescription: "Submit another expense"
        ))]);
        return Success("Expense rejected. The expense owners were notified by email.");
    }

    public async Task<StatusMessage> CreateAsync(File receipt, string description, decimal amount, string currencyCode, decimal currencyExchangeRate, string[] emailAddresses, bool alreadyReimbursed)
    {
        if (amount <= 0)
        {
            return Error("Amount must be greater than 0!");
        }
        if (currencyExchangeRate <= 0)
        {
            return Error("Currency exchange rates must be greater than zero!");
        }
        if (emailAddresses.Length == 0)
        {
            return Error("Creating an expense requires at least 1 email address.");
        }

        var owners = new List<Participant>();
        var unknownEmailAddresses = new List<string>();
        var nonConfirmedParticipants = new List<string>();
        foreach (var emailAddress in emailAddresses)
        {
            var participant = await participants.FindAsync(emailAddress);
            if (participant is null)
            {
                unknownEmailAddresses.Add(emailAddress);
            }
            else if (participant.Status < ParticipantStatus.Confirmed)
            {
                nonConfirmedParticipants.Add(emailAddress);
            }
            else
            {
                owners.Add(participant);
            }
        }
        if (unknownEmailAddresses.Count > 0 || nonConfirmedParticipants.Count > 0)
        {
            return Error(
                "Not all email addresses are valid.\n\n" +
                (unknownEmailAddresses.Count > 0 ? ("Unknown email addresses: " + string.Join(", ", unknownEmailAddresses) + "\n\n") : "") +
                (nonConfirmedParticipants.Count > 0 ? ("Non-confirmed participants: " + string.Join(", ", nonConfirmedParticipants)) : "")
            );
        }

        // Create one expense per owner so owners can cancel approved expenses just for themselves,
        // and so we don't leak who else the admins wanted to reimburse.
        foreach (var owner in owners)
        {
            var receiptId = await fileStorage.StoreFileAsync(receipt);
            var expense = new TravelExpense(
                receiptId,
                timeProvider.GetUtcNow(),
                description,
                amount,
                currencyCode,
                false
            )
            {
                Owners = { owner },
                Status = alreadyReimbursed ? TravelExpenseStatus.Reimbursed : TravelExpenseStatus.Approved
            };
            expenses.Add(expense);
        }

        await UpdateCurrencyAsync(currencyCode, currencyExchangeRate);

        return Success($"Expense '{description}' added and {(alreadyReimbursed ? "marked as reimbursed" : "pre-approved")} for {owners.Count} people, {currencyCode} {amount.ToString(CultureInfo.InvariantCulture)} each.");
    }

    private async Task UpdateCurrencyAsync(string currencyCode, decimal currencyExchangeRate)
    {
        var currency = await currencies.FindAsync(currencyCode);
        if (currency is null)
        {
            currencies.Add(new(currencyCode, currencyExchangeRate));
        }
        else
        {
            currency.ExchangeRate = currencyExchangeRate;
        }
    }

    public sealed record OwnerInfo(
        string? FullName,
        string? ReimbursementTier
    );

    public sealed record ExpenseInfo(
        string Description,
        decimal Amount,
        string CurrencyCode,
        bool CountsDouble,
        IReadOnlyCollection<string?> OwnerNames,
        TravelExpenseStatus Status
    );

    public sealed record ExpenseWithContext(
        string ReceiptId,
        string Description,
        decimal Amount,
        string CurrencyCode,
        decimal? CurrencyExchangeRate,
        bool CountsDouble,
        IReadOnlyCollection<OwnerInfo> Owners,
        IReadOnlyCollection<ExpenseInfo> OwnerExpenses,
        TravelExpenseStatus Status
    );
}