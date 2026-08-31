using System;
using System.Collections.Generic;

namespace EventManager.Models;

/// <summary>
/// Travel expense a participant can be reimbursed for.
/// </summary>
public sealed class TravelExpense(string receiptId, DateTimeOffset creationDate, string description, decimal amount, string currencyCode, bool countsDouble)
{
    public const int MaxDescriptionLength = 40;

    /// <summary>
    /// The ID of the corresponding receipt file, which doubles as an ID for this expense.
    /// </summary>
    public string ReceiptId { get; set; } = receiptId;

    /// <summary>
    /// When this expense was created.
    /// </summary>
    public DateTimeOffset CreationDate { get; set; } = creationDate;

    /// <summary>
    /// A short description of the expense.
    /// </summary>
    public string Description { get; set; } = description;

    /// <summary>
    /// The amount of the expense.
    /// </summary>
    public decimal Amount { get; set; } = amount;

    /// <summary>
    /// The amount to reimburse per participant.
    /// </summary>
    public decimal AmountToReimbursePerPerson => Amount / Owners.Count * (CountsDouble ? 2 : 1);

    /// <summary>
    /// The code of the currency the expense is in.
    /// </summary>
    public string CurrencyCode { get; set; } = currencyCode;

    /// <summary>
    /// Whether this expense should be reimbursed twice, typically because it is an expense for a one-way trip that should also count for a future, not yet bought return trip.
    /// </summary>
    public bool CountsDouble { get; set; } = countsDouble;

    /// <summary>
    /// The participants owning the expense, who are assumed to have spent equal shares.
    /// </summary>
    public ISet<Participant> Owners { get; } = new HashSet<Participant>();

    /// <summary>
    /// The status of this travel expense.
    /// </summary>
    public TravelExpenseStatus Status { get; set; }
}