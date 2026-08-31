using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class TravelReimbursementPage(IQueryable<TravelExpense> expenses, IQueryable<Currency> currencies, TravelReimbursementPolicy? policy) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (policy is null)
        {
            return ForbiddenView();
        }
        return EditableView("Travel reimbursement", "Manage");
    }

    public override async Task<object?> GetModelAsync(Admin admin)
    {
        // Invert the Expenses->Owners mapping so we get all expenses per owner.
        // This is not super efficient, we could combine it with the loop below to update each owner's status instead,
        // but anyway we'll need to pull basically every expense from the DB (modulo those of people who didn't check in) so it should be efficient enough.
        var allExpenses = await expenses.ToCollectionAsync();
        var expensesByOwner = new Dictionary<Participant, List<TravelExpense>>();
        foreach (var expense in allExpenses)
        {
            foreach (var owner in expense.Owners)
            {
                if (owner.Status < ParticipantStatus.CheckedIn)
                {
                    continue;
                }
                if (expensesByOwner.TryGetValue(owner, out var existing))
                {
                    existing.Add(expense);
                }
                else
                {
                    expensesByOwner.Add(owner, [expense]);
                }
            }
        }

        var currenciesByCode = await currencies.ToDictionaryAsync(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var results = new List<ParticipantReimbursement>();
        // Iterate over just the keys so we can reuse the participant ordering logic
        foreach (var participant in expensesByOwner.Keys.OrderByName())
        {
            decimal amount = 0;
            foreach (var expense in expensesByOwner[participant])
            {
                if (!currenciesByCode.TryGetValue(expense.CurrencyCode, out var expenseCurrency))
                {
                    throw new InvalidOperationException($"The following currency code is unknown, which should never happen: {expense.CurrencyCode}");
                }
                var convertedExpenseAmount = expense.AmountToReimbursePerPerson * expenseCurrency.ExchangeRate;
                if (expense.Status is TravelExpenseStatus.Reimbursed)
                {
                    amount -= convertedExpenseAmount;
                }
                else if (expense.Status is TravelExpenseStatus.Approved)
                {
                    amount += convertedExpenseAmount;
                }
            }
            decimal? cap;
            if (participant.TravelReimbursementTier is null || policy is null || !policy.Tiers.TryGetValue(participant.TravelReimbursementTier, out var tierCap))
            {
                // should never happen but let's not crash
                cap = null;
            }
            else
            {
                cap = tierCap;
                var rounding = Math.Max(1, policy.RoundingAmount); // in .NET terms "no rounding" == "round to 1"
                amount = decimal.Ceiling(Math.Min(amount, tierCap) / rounding) * rounding;
            }
            if (amount > 0)
            {
                results.Add(new(participant.FullName, participant.EmailAddress, participant.Status >= ParticipantStatus.Demoed, amount, cap, participant.AdminRemarks));
            }
        }
        return results;
    }

    public sealed record ParticipantReimbursement(string? FullName, string EmailAddress, bool HasDemoed, decimal Amount, decimal? Cap, string? Remarks);
}