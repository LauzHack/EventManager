using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class TravelReimbursementPolicyPage(IQueryable<Participant> participants, ConfigValue<TravelReimbursementPolicy> policyConfig, EventStatus eventStatus) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (policyConfig.Value is null)
        {
            if (eventStatus >= EventStatus.CheckInStarted || !admin.IsOwner)
            {
                return ForbiddenView();
            }
            return EditableView("Travel reimbursement policy", "Set");
        }
        PageSummaryItem[] summary = [
            ..from t in policyConfig.Value.Tiers
              orderby t.Value
              select new PageSummaryItem(t.Key, policyConfig.Value.EventCurrencyCode + " " + t.Value.ToString(CultureInfo.InvariantCulture)),
            ("Rounding amount", policyConfig.Value.EventCurrencyCode + " " + policyConfig.Value.RoundingAmount.ToString(CultureInfo.InvariantCulture))
        ];
        if (eventStatus >= EventStatus.CheckInStarted || !admin.IsOwner)
        {
            return SummaryOnlyView("Travel reimbursement policy", summary);
        }
        return EditableView("Travel reimbursement policy", "Manage", summary);
    }

    public async Task<StatusMessage> EditAsync(TravelReimbursementPolicy policy)
    {
        if (policy.Tiers.Count == 0)
        {
            return Error("Cannot set no tiers, that's equivalent to not having a travel reimbursement policy");
        }
        if (policy.Tiers.Values.Any(a => a < 0))
        {
            return Error("Cannot have a tier with an amount less than zero, that makes no sense");
        }

        if (policyConfig.Value is TravelReimbursementPolicy existing)
        {
            var removedTiers = existing.Tiers.Keys.Where(k => !policy.Tiers.ContainsKey(k)).ToArray();
            var affectedParticipants = await participants.Where(p => removedTiers.Contains(p.TravelReimbursementTier)).ToCollectionAsync();
            if (affectedParticipants.Count > 0)
            {
                foreach (var participant in affectedParticipants)
                {
                    participant.TravelReimbursementTier = null;
                }

                policyConfig.Set(policy);
                return Success($"Updated travel reimbursement policy **and cleared the tier choice of {affectedParticipants.Count} people**");
            }
        }

        policyConfig.Set(policy);
        return Success("Updated travel reimbursement policy");
    }
}