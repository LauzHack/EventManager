using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class TravelReimbursementPage(
    IQueryable<Participant> participants,
    IQueryable<TravelExpense> expenses,
    IQueryable<Currency> currencies,
    TravelReimbursementPolicy? policy
) : Page<Admin>
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
        // but anyway we'll need to pull basically every expense from the DB so it should be efficient enough.
        var allExpenses = await expenses.ToCollectionAsync();
        var expensesByOwner = new Dictionary<Participant, List<TravelExpense>>();
        foreach (var expense in allExpenses)
        {
            foreach (var owner in expense.Owners)
            {
                if (owner.Status < ParticipantStatus.Accepted)
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

        var currenciesByCode = await currencies.ToDictionaryAsync(
            c => c.Code,
            StringComparer.OrdinalIgnoreCase
        );
        var reimbursements = new List<ParticipantReimbursement>();
        var forecast = new List<ParticipantReimbursement>();
        var forecastParticipants = new List<(Participant, decimal)>();
        // Iterate over just the keys so we can reuse the participant ordering logic
        foreach (var participant in expensesByOwner.Keys.OrderByName())
        {
            var reimbursement = ComputeReimbursement(
                participant,
                expensesByOwner[participant],
                currenciesByCode
            );
            if (reimbursement is null)
            {
                continue;
            }
            // The forecast includes everyone who may come to the event, but only checked-in participants can actually be reimbursed
            forecast.Add(reimbursement);
            forecastParticipants.Add((participant, reimbursement.Amount));
            if (participant.Status >= ParticipantStatus.CheckedIn)
            {
                reimbursements.Add(reimbursement);
            }
        }
        return new Model(
            reimbursements,
            forecast,
            await ComputeTierBreakdownAsync(forecastParticipants)
        );
    }

    private async Task<IReadOnlyCollection<TierCount>> ComputeTierBreakdownAsync(
        IReadOnlyCollection<(Participant Participant, decimal Amount)> forecastParticipants
    )
    {
        if (policy is null)
        {
            return [];
        }
        // Participants whose tier is missing or no longer exists are grouped under "no tier", represented as an empty string here
        string TierOf(string? tier) => tier is not null && policy.Tiers.ContainsKey(tier) ? tier : "";
        var acceptedTiers = await participants
            .Where(p => p.Status >= ParticipantStatus.Accepted)
            .Select(p => p.TravelReimbursementTier)
            .ToCollectionAsync();
        var acceptedCounts = acceptedTiers.CountBy(TierOf, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);
        var forecastByTier = forecastParticipants
            .GroupBy(f => TierOf(f.Participant.TravelReimbursementTier), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(f => f.Amount)), StringComparer.Ordinal);

        var results = new List<TierCount>();
        foreach (var (name, cap) in policy.Tiers.OrderBy(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal))
        {
            var count = acceptedCounts.GetValueOrDefault(name);
            var expected = forecastByTier.GetValueOrDefault(name);
            results.Add(new(name, cap, count, expected.Count, expected.Total, cap * count));
        }
        var noTierCount = acceptedCounts.GetValueOrDefault("");
        if (noTierCount > 0)
        {
            var expected = forecastByTier.GetValueOrDefault("");
            results.Add(new(null, null, noTierCount, expected.Count, expected.Total, null));
        }
        return results;
    }

    private ParticipantReimbursement? ComputeReimbursement(
        Participant participant,
        IEnumerable<TravelExpense> participantExpenses,
        IReadOnlyDictionary<string, Currency> currenciesByCode
    )
    {
        decimal amount = 0;
        foreach (var expense in participantExpenses)
        {
            if (!currenciesByCode.TryGetValue(expense.CurrencyCode, out var expenseCurrency))
            {
                throw new InvalidOperationException(
                    $"The following currency code is unknown, which should never happen: {expense.CurrencyCode}"
                );
            }
            var convertedExpenseAmount =
                expense.AmountToReimbursePerPerson * expenseCurrency.ExchangeRate;
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
        if (
            participant.TravelReimbursementTier is null
            || policy is null
            || !policy.Tiers.TryGetValue(participant.TravelReimbursementTier, out var tierCap)
        )
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
        if (amount <= 0)
        {
            return null;
        }
        return new(
            participant.FullName,
            participant.EmailAddress,
            participant.Status >= ParticipantStatus.Demoed,
            amount,
            cap,
            participant.AdminRemarks
        );
    }

    /// <summary>
    /// The page's model.
    /// </summary>
    /// <param name="Reimbursements">Participants who can be reimbursed now, i.e., who checked in.</param>
    /// <param name="Forecast">Indicative reimbursements for all accepted participants, whether they checked in or not.</param>
    /// <param name="Tiers">Indicative breakdown of accepted participants per reimbursement tier, ordered by cap.</param>
    public sealed record Model(
        IReadOnlyCollection<ParticipantReimbursement> Reimbursements,
        IReadOnlyCollection<ParticipantReimbursement> Forecast,
        IReadOnlyCollection<TierCount> Tiers
    );

    /// <summary>
    /// Indicative statistics for one reimbursement tier.
    /// </summary>
    /// <param name="Name">The tier's name, or <c>null</c> for accepted participants without a valid tier.</param>
    /// <param name="Cap">The tier's cap, or <c>null</c> for participants without a valid tier.</param>
    /// <param name="Participants">Accepted participants in this tier.</param>
    /// <param name="ParticipantsWithExpenses">Those of them who have something to be reimbursed.</param>
    /// <param name="ExpectedTotal">The total to be reimbursed to them based on their approved expenses.</param>
    /// <param name="MaxTotal">The total if every participant in this tier is reimbursed up to the cap.</param>
    public sealed record TierCount(
        string? Name,
        decimal? Cap,
        int Participants,
        int ParticipantsWithExpenses,
        decimal ExpectedTotal,
        decimal? MaxTotal
    );

    public sealed record ParticipantReimbursement(
        string? FullName,
        string EmailAddress,
        bool HasDemoed,
        decimal Amount,
        decimal? Cap,
        string? Remarks
    );
}