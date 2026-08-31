using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class TravelPage(DbValues<Participant> participants, DbValues<TravelExpense> expenses,
                               TravelReimbursementPolicy? reimbursementPolicy, EventStatus eventStatus, EventDetails eventDetails,
                               FileStorage fileStorage, EmailSender emailSender, TimeProvider timeProvider) : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant)
    {
        if (participant.Status < ParticipantStatus.Confirmed)
        {
            return ForbiddenView();
        }
        if (eventStatus < EventStatus.CheckInStarted)
        {
            return RequiredView("See you soon!");
        }
        if (reimbursementPolicy is null || eventStatus >= EventStatus.JudgingStarted)
        {
            return ForbiddenView();
        }
        if (participant.Status < ParticipantStatus.DeclaredTravelExpenses)
        {
            return RequiredView("Travel reimbursement");
        }
        var relevantExpenses = await expenses.Where(e => e.Owners.Contains(participant)).ToCollectionAsync();
        return EditableView("Travel reimbursement", "Manage expenses", relevantExpenses.Select(e => new PageSummaryItem(e.Description, $"{e.CurrencyCode} {e.Amount.ToString(CultureInfo.InvariantCulture)}")));
    }

    public override async Task<object?> GetModelAsync(Participant participant)
        => await expenses.Where(e => e.Owners.Contains(participant)).ToCollectionAsync();

    public async Task<StatusMessage> WithdrawAsync(Participant participant)
    {
        if (participant.Status >= ParticipantStatus.CheckedIn)
        {
            return Error("You can no longer withdraw.");
        }
        await emailSender.SendEmailAsync(
            recipient: participant.EmailAddress,
            subject: "Withdrawal",
            body: $"You have withdrawn from {eventDetails}.",
            operation: Operation.CreatePageAction<Participant?, WithdrawnPage>(nameof(WithdrawnPage.UndoAsync)),
            operationDescription: "Undo withdrawal"
        );
        participant.Status = ParticipantStatus.WithdrawnAfterConfirmation;
        participant.VisaInformation = new ParticipantVisaInformation();
        return Success("You have withdrawn.");
    }

    public async Task<StatusMessage> ChooseTravelReimbursementTierAsync(Participant participant, string tier)
    {
        if (reimbursementPolicy is null)
        {
            return Error("Travel reimbursement is not enabled.");
        }
        if (!reimbursementPolicy.Tiers.ContainsKey(tier))
        {
            return Error($"Unknown travel reimbursement tier: '{tier}'");
        }
        participant.TravelReimbursementTier = tier;
        return Success($"Your travel reimbursement tier is now '**{tier}**'");
    }

    public async Task<StatusMessage> SubmitTravelExpenseAsync(Participant participant, string description, decimal amount, string currencyCode, bool countsDouble, string[] ownerEmailAddresses, File receipt)
    {
        if (reimbursementPolicy is null)
        {
            return Error("Travel reimbursement is not enabled.");
        }
        if (participant.TravelReimbursementTier is null)
        {
            return Error("Please choose your travel reimbursement tier first");
        }
        if (description.Length > TravelExpense.MaxDescriptionLength)
        {
            return Error($"The description cannot be longer than {TravelExpense.MaxDescriptionLength} characters.");
        }
        if (amount <= 0)
        {
            return Error("Please enter an amount greater than zero.");
        }

        var owners = await participants.Where(p => ownerEmailAddresses.Contains(p.EmailAddress)).ToCollectionAsync();
        if (owners.Count != ownerEmailAddresses.Length)
        {
            return Error($"Unknown email addresses: {string.Join(", ", ownerEmailAddresses.Except(owners.Select(p => p.EmailAddress), StringComparer.OrdinalIgnoreCase))}.");
        }

        var withoutTiers = owners.Where(o => o.TravelReimbursementTier is null).ToArray();
        if (withoutTiers.Length > 0)
        {
            return Error($"To share this expense, **{string.Join(", ", withoutTiers.Select(o => o.FullName))}** must pick a travel reimbursement tier first.");
        }

        var fileId = await fileStorage.StoreFileAsync(receipt);
        var expense = new TravelExpense(fileId, timeProvider.GetUtcNow(), description, amount, currencyCode, countsDouble) { Owners = { participant } };
        foreach (var owner in owners)
        {
            expense.Owners.Add(owner);
        }
        expenses.Add(expense);
        return Success($"Added expense '**{description}**'.");
    }

    public async Task<StatusMessage> CancelTravelExpenseAsync(Participant participant, string receiptId)
    {
        var expense = await expenses.FindAsync(receiptId);
        if (expense is null)
        {
            return Error("Unknown expense. Perhaps someone else has already canceled it?");
        }
        if (!expense.Owners.Contains(participant))
        {
            return Error("Unknown expense.");
        }
        if (expense.Status is TravelExpenseStatus.Reimbursed)
        {
            return Error("Cannot cancel an expense that was already reimbursed.");
        }
        await fileStorage.DeleteFileAsync(receiptId);
        expenses.Remove(expense);
        return Success($"Canceled expense '**{expense.Description}**'");
    }

    public async Task<StatusMessage> FinishDeclaringTravelExpensesAsync(Participant participant)
    {
        if (participant.Status is not ParticipantStatus.CheckedIn)
        {
            return Error("You cannot finish declaring travel expenses unless you are in the 'checked in' status.");
        }
        participant.Status = ParticipantStatus.DeclaredTravelExpenses;
        return Success("Thank you for confirming!");
    }
}