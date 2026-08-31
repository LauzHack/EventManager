using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class TravelPageTests : ParticipantTestsBase
{
    [TestMethod]
    [DataRow(ParticipantStatus.Created)]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation)]
    [DataRow(ParticipantStatus.Rejected)]
    [DataRow(ParticipantStatus.DidNotConfirm)]
    [DataRow(ParticipantStatus.Accepted)]
    public async Task PageIsHiddenWhenNotConfirmed(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            await Db.CommitAsync();
        }

        var view = await GetPage().ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    public async Task PageIsRequiredWhenEventHasNotStarted()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var view = await GetPage(eventStatus: EventStatus.ApplicationsClosed).ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
    }

    [TestMethod]
    public async Task PageIsRequiredWhenEventHasStartedAndTravelReimbursementIsEnabled()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var view = await GetPage(policy: ReimbursementPolicy, eventStatus: EventStatus.CheckInStarted).ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
    }

    [TestMethod]
    public async Task PageIsHiddenWhenEventHasStartedAndTravelReimbursementIsNotEnabled()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var view = await GetPage(eventStatus: EventStatus.CheckInStarted).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWhenEventHasStartedAndParticipantHasDeclaredTravelExpenses()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.DeclaredTravelExpenses;
            await Db.CommitAsync();
        }

        var view = await GetPage(policy: ReimbursementPolicy, eventStatus: EventStatus.CheckInStarted).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsHiddenWhenTravelExpensesSubmissionIsClosedEvenIfParticipantHasNotDeclaredTravelExpenses()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var view = await GetPage(policy: ReimbursementPolicy, eventStatus: EventStatus.JudgingStarted).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task WithdrawSetsStatusToWithdrawnAfterConfirmation()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        {
            var result = await GetPage(enableEmails: true).WithdrawAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual(ParticipantStatus.WithdrawnAfterConfirmation, newParticipant.Status);
    }

    [TestMethod]
    public async Task WithdrawalDeletesPendingVisaLetterRequest()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            participant.VisaInformation.PassportPhotoId = "some-id";
            participant.VisaInformation.ParticipantDetails = [.. VisaInvitationFormat.ParticipantDetails.Select(_ => "x")];
            Db.Admins.Add(
                new Admin("admin@example.org")
            );
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(enableEmails: true).WithdrawAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.IsEmpty(newParticipant.VisaInformation.ParticipantDetails);
        Assert.IsNull(newParticipant.VisaInformation.PassportPhotoId);
    }

    [TestMethod]
    public async Task WithdrawalDeletesVisaLetter()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            participant.VisaInformation = new()
            {
                ParticipantDetails = [.. VisaInvitationFormat.ParticipantDetails.Select(_ => "x")],
                PassportPhotoId = "xyz",
                AdminDetails = "someone",
                Letter = new Letter("id", "hi", DateTimeOffset.MinValue)
            };
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(enableEmails: true).WithdrawAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.IsEmpty(newParticipant.VisaInformation.ParticipantDetails);
        Assert.IsNull(newParticipant.VisaInformation.PassportPhotoId);
    }

    [TestMethod]
    public async Task WithdrawSendsUndoEmail()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        await GetPage(enableEmails: true).WithdrawAsync(await GetParticipantAsync());

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, email.Recipient);
        Assert.AreEqual(Operation.CreatePageAction<Participant?, WithdrawnPage>(nameof(WithdrawnPage.UndoAsync)), email.Operation);
        // ensure the participant doesn't get confused as to which event they withdrew from
        Assert.Contains(EventDetails.ToString(), email.Body, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task WithdrawFailsIfAlreadyCheckedIn()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(enableEmails: true).WithdrawAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.UserError, result.Status);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("A")]
    public async Task ChooseTierSetsTier(string? previousTier)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            participant.TravelReimbursementTier = previousTier;
            await Db.CommitAsync();
        }

        {
            var result = await GetPage(policy: ReimbursementPolicy).ChooseTravelReimbursementTierAsync(await GetParticipantAsync(), "B");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.AreEqual("B", newParticipant.TravelReimbursementTier);
    }

    [TestMethod]
    public async Task ChooseTierFailsIfTravelReimbursementIsNotEnabled()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var result = await GetPage().ChooseTravelReimbursementTierAsync(await GetParticipantAsync(), "A");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task ChooseTierFailsForUnknownTier()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var result = await GetPage(policy: ReimbursementPolicy).ChooseTravelReimbursementTierAsync(await GetParticipantAsync(), "Missing");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SubmitExpenseStoresExpenseAndFile()
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.Confirmed;
            Db.Participants.Add(new Participant("bob@example.org")
            {
                TravelReimbursementTier = "A",
                Status = ParticipantStatus.Confirmed
            });
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "image/png", [0, 1, 2, 3]);
        var result = await GetPage(policy: ReimbursementPolicy, enableTime: true).SubmitTravelExpenseAsync(await GetParticipantAsync(), "Descr", 42.123m, "XXX", true, ["bob@example.org"], file);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var newParticipant = await GetParticipantAsync();
        var expense = Assert.ContainsSingle(Db.TravelExpenses);
        Assert.Contains(newParticipant, expense.Owners);
        Assert.AreEqual("Descr", expense.Description);
        Assert.AreEqual(TimeProvider.GetUtcNow(), expense.CreationDate);
        Assert.AreEqual(42.123m, expense.Amount);
        Assert.AreEqual("XXX", expense.CurrencyCode);
        Assert.IsTrue(expense.CountsDouble);
        Assert.AreSequenceEqual([ParticipantEmailAddress, "bob@example.org"], expense.Owners.Select(o => o.EmailAddress), SequenceOrder.InAnyOrder);
        var storedFile = await FileStorage.GetFileAsync(expense.ReceiptId);
        Assert.IsNotNull(storedFile);
        Assert.AreEqual(file.MimeType, storedFile.MimeType);
    }

    [TestMethod]
    public async Task SubmitExpenseFailsIfTravelReimbursementIsNotEnabled()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            Db.Participants.Add(new Participant("bob@example.org"));
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "image/png", [0, 1, 2, 3]);
        var result = await GetPage().SubmitTravelExpenseAsync(await GetParticipantAsync(), "Descr", 42.123m, "XXX", true, ["bob@example.org"], file);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SubmitExpenseFailsIfParticipantHasNotChosenATier()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "image/png", [0, 1, 2, 3]);
        var result = await GetPage(policy: ReimbursementPolicy).SubmitTravelExpenseAsync(await GetParticipantAsync(), "Descr", 42.123m, "XXX", true, [], file);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SubmitExpenseFailsIfOtherOwnerHasNotChosenATier()
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.Confirmed;
            Db.Participants.Add(new Participant("bob@example.org"));
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "image/png", [0, 1, 2, 3]);
        var result = await GetPage(policy: ReimbursementPolicy).SubmitTravelExpenseAsync(await GetParticipantAsync(), "Descr", 42.123m, "XXX", true, ["bob@example.org"], file);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SubmitExpenseFailsIfDescriptionIsTooLong()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            participant.TravelReimbursementTier = "A";
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "image/png", [0, 1, 2, 3]);
        var description = new string('x', TravelExpense.MaxDescriptionLength + 1);
        var result = await GetPage(policy: ReimbursementPolicy).SubmitTravelExpenseAsync(await GetParticipantAsync(), description, 42.123m, "XXX", true, [], file);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task SubmitExpenseFailsIfAmountIsNotGreaterThanZero(int amount)
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.Confirmed;
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "image/png", [0, 1, 2, 3]);
        var result = await GetPage(policy: ReimbursementPolicy).SubmitTravelExpenseAsync(await GetParticipantAsync(), "Descr", amount, "XXX", true, [], file);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SubmitExpenseFailsIfOwnerEmailIsUnknown()
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.Confirmed;
            Db.Participants.Add(new Participant("bob@example.org")
            {
                TravelReimbursementTier = "A",
                Status = ParticipantStatus.Confirmed
            });
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "image/png", [0, 1, 2, 3]);
        var result = await GetPage(policy: ReimbursementPolicy).SubmitTravelExpenseAsync(await GetParticipantAsync(), "Descr", 42.123m, "XXX", true, ["bob@example.org", "doesnotexist@example.org"], file);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelExpenseDeletesExpenseAndFile()
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.Confirmed;
            await Db.CommitAsync();
        }

        {
            var file = new File.InMemory("name", "image/png", [0, 1, 2, 3]);
            var result = await GetPage(policy: ReimbursementPolicy, enableTime: true).SubmitTravelExpenseAsync(await GetParticipantAsync(), "Descr", 42.123m, "XXX", true, [], file);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var expense = Assert.ContainsSingle(Db.TravelExpenses);

        {
            var result = await GetPage(policy: ReimbursementPolicy).CancelTravelExpenseAsync(await GetParticipantAsync(), expense.ReceiptId);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        Assert.IsEmpty(Db.TravelExpenses);
        Assert.IsNull(await FileStorage.GetFileAsync(expense.ReceiptId));
    }

    [TestMethod]
    public async Task CancelExpenseFailsIfIdIsUnknown()
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.Confirmed;
            await Db.CommitAsync();
        }

        var result = await GetPage(policy: ReimbursementPolicy).CancelTravelExpenseAsync(await GetParticipantAsync(), "unknown");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelExpenseFailsIfExpenseDoesNotBelongToUser()
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.Confirmed;
            var bob = new Participant("bob@example.org");
            Db.Participants.Add(bob);
            Db.TravelExpenses.Add(new("id", DateTimeOffset.UtcNow, "descr", 42.0m, "CHF", false) { Owners = { bob } });
            await Db.CommitAsync();
        }

        var result = await GetPage(policy: ReimbursementPolicy).CancelTravelExpenseAsync(await GetParticipantAsync(), "id");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelExpenseFailsIfExpenseIsAlreadyReimbursed()
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.Confirmed;
            var expense = new TravelExpense("id", TimeProvider.GetUtcNow(), "descr", 42, "CHF", false)
            {
                Status = TravelExpenseStatus.Reimbursed,
                Owners = { participant }
            };
            Db.TravelExpenses.Add(expense);
            await Db.CommitAsync();
        }

        var result = await GetPage(policy: ReimbursementPolicy).CancelTravelExpenseAsync(await GetParticipantAsync(), "id");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinishDeclaringExpensesSetsStatus(bool hasExpense)
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.CheckedIn;
            if (hasExpense)
            {
                Db.TravelExpenses.Add(new("123", DateTimeOffset.UtcNow, "Expense", 10, "CHF", false) { Owners = { participant } });
            }
            await Db.CommitAsync();
        }

        var result = await GetPage(policy: ReimbursementPolicy).FinishDeclaringTravelExpensesAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var newParticipant = await GetParticipantAsync();
        Assert.AreEqual(ParticipantStatus.DeclaredTravelExpenses, newParticipant.Status);
    }

    [TestMethod]
    public async Task FinishDeclaringExpensesFailsIfParticipantIsNotCheckedIn()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var result = await GetPage(policy: ReimbursementPolicy).FinishDeclaringTravelExpensesAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task FinishDeclaringExpensesFailsIfParticipantAlreadyDidIt()
    {
        {
            var participant = await GetParticipantAsync();
            participant.TravelReimbursementTier = "A";
            participant.Status = ParticipantStatus.DeclaredTravelExpenses;
            await Db.CommitAsync();
        }

        var result = await GetPage(policy: ReimbursementPolicy).FinishDeclaringTravelExpensesAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    private TravelPage GetPage(TravelReimbursementPolicy? policy = null, EventStatus eventStatus = EventStatus.ApplicationsOpen, bool enableEmails = false, bool enableTime = false)
        => new(
               Db.Participants, Db.TravelExpenses,
               policy, eventStatus, EventDetails, FileStorage,
               enableEmails ? EmailSender : DisabledEmailSender,
               enableTime ? TimeProvider : DisabledTimeProvider
           );
}