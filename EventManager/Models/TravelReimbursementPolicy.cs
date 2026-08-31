using System;
using System.Collections.Immutable;

namespace EventManager.Models;

/// <summary>
/// The travel reimbursement policy for an event.
/// </summary>
/// <param name="EventCurrencyCode">The code of the currency used by the event.</param>
/// <param name="TiersDescription">The description of the tiers for travel reimbursement.</param>
/// <param name="DetailsUrl">A link to a page with more details about the event's travel reimbursement policy.</param>
/// <param name="Tiers">The tier names and amounts.</param>
/// <param name="RoundingAmount">The rounding amount used for reimbursement.</param>
public sealed record TravelReimbursementPolicy(string EventCurrencyCode, string TiersDescription, Uri DetailsUrl, ImmutableDictionary<string, decimal> Tiers, uint RoundingAmount);