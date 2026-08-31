using System;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class WaitForAcceptancePageTests : ParticipantTestsBase
{
    [TestMethod]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Accepted)]
    public async Task PageIsRequiredWhenNotConfirmed(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var view = await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, DisabledEmailSender)
            .ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task PageIsHiddenWhenAtLeastConfirmed(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var view = await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, DisabledEmailSender)
            .ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    public async Task ConfirmSetsStatusOfAcceptedParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Accepted;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            var result = await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
                .ConfirmAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.AreEqual(ParticipantStatus.Confirmed, newParticipant.Status);
    }

    [TestMethod]
    [DataRow(4u)]
    [DataRow(1u)]
    [DataRow(0u)]
    public async Task ConfirmSendsEmailToAcceptedParticipantWithEventAndExtraInfo(uint groupSize)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Accepted;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            var limits = EventLimits with { ApplicationGroupSize = groupSize };
            await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, limits, EmailSender)
                .ConfirmAsync(await GetParticipantAsync());
        }

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, email.Recipient);
        Assert.Contains(EventDetails.ConfirmationText, email.Body, StringComparison.Ordinal);
        // Avoids confusion when the same org has multiple events around the same time
        Assert.Contains(EventDetails.ToString(), email.Body, StringComparison.Ordinal);
        if (groupSize > 1u)
        {
            Assert.Contains("changing your application group is no longer possible", email.Body, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.DoesNotContain("changing your application group is no longer possible", email.Body, StringComparison.OrdinalIgnoreCase);
        }
        Assert.AreSame(EventDetails, email.AttachedEvent);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task ConfirmDoesNothingForAlreadyConfirmedParticipant(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            var result = await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
                .ConfirmAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.None, result.Status);
        }

        var newParticipant = await GetParticipantAsync();
        Assert.AreEqual(status, newParticipant.Status);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Created)]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Rejected)]
    [DataRow(ParticipantStatus.DidNotConfirm)]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation)]
    public async Task ConfirmFailsForNonAcceptedParticipant(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var result = await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
            .ConfirmAsync(await GetParticipantAsync());

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task UnfinalizeChangesStatus()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.Finalized
            };
            var carol = new Participant("carol@example.org")
            {
                Status = ParticipantStatus.Finalized
            };
            Db.Participants.Add(bob, carol);
            Db.ApplicationGroups.Add(new("id") { Members = { participant, bob } });
            await Db.CommitAsync();
        }

        {
            var result = await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
                .UnfinalizeAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.ImportantInformation, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual(ParticipantStatus.ProfileFilled, newParticipant.Status);

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        Assert.AreEqual(ParticipantStatus.ProfileFilled, newOther.Status);

        var newUnrelated = await Db.Participants.FindAsync("carol@example.org");
        Assert.IsNotNull(newUnrelated);
        Assert.AreEqual(ParticipantStatus.Finalized, newUnrelated.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnfinalizeSendsEmail(bool groupHasOtherMembers)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.Finalized
            };
            var carol = new Participant("carol@example.org")
            {
                Status = ParticipantStatus.Finalized
            };
            Db.Participants.Add(bob, carol);
            var group = new ApplicationGroup("id") { Members = { participant } };
            if (groupHasOtherMembers)
            {
                group.Members.Add(bob);
            }
            Db.ApplicationGroups.Add(group);
            await Db.CommitAsync();
        }

        {
            await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
                .UnfinalizeAsync(await GetParticipantAsync());
        }

        if (groupHasOtherMembers)
        {
            Assert.HasCount(2, EmailSender.Outbox);
            Assert.AreEqual(ParticipantEmailAddress, EmailSender.Outbox[0].Recipient);
            Assert.AreEqual("bob@example.org", EmailSender.Outbox[1].Recipient);
            Assert.Contains("a group member", EmailSender.Outbox[0].Body, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var email = Assert.ContainsSingle(EmailSender.Outbox);
            Assert.AreEqual(ParticipantEmailAddress, email.Recipient);
            Assert.DoesNotContain("a group member", email.Body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public async Task UnfinalizeDoesNotSendEmailWhenGroupAlreadyUnfinalized()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.ProfileFilled;
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.ProfileFilled
            };
            var carol = new Participant("carol@example.org")
            {
                Status = ParticipantStatus.Finalized
            };
            Db.Participants.Add(bob, carol);
            Db.ApplicationGroups.Add(new("id") { Members = { participant, bob } });
            await Db.CommitAsync();
        }

        var result = await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
            .UnfinalizeAsync(await GetParticipantAsync());

        Assert.AreEqual(Status.None, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task WithdrawSetsStatusToWithdrawnBeforeConfirmation()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            var result = await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
                .WithdrawAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.AreEqual(ParticipantStatus.WithdrawnBeforeConfirmation, newParticipant.Status);
    }

    [TestMethod]
    public async Task WithdrawSendsUndoEmail()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
                .WithdrawAsync(await GetParticipantAsync());
        }

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, email.Recipient);
        Assert.AreEqual(Operation.CreatePageAction<Participant?, WithdrawnPage>(nameof(WithdrawnPage.UndoAsync)), email.Operation);
    }

    [TestMethod]
    public async Task WithdrawRemovesParticipantFromGroup()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            var participant2 = new Participant("bob@example.org") { Status = ParticipantStatus.Finalized };
            Db.Participants.Add(participant2);
            Db.ApplicationGroups.Add(new("id") { Members = { participant, participant2 } });
            await Db.CommitAsync();
        }

        {
            await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
                .WithdrawAsync(await GetParticipantAsync());
            await Db.CommitAsync();
        }

        var newParticipant2 = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newParticipant2);
        var newGroup2 = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant2));
        Assert.IsNotNull(newGroup2);
        Assert.HasCount(1, newGroup2.Members);
    }

    [TestMethod]
    public async Task WithdrawDoesNotYieldOrphanedGroup()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            await new WaitForAcceptancePage(Db.ApplicationGroups, EventDetails, EventLimits, EmailSender)
                .WithdrawAsync(await GetParticipantAsync());
            await Db.CommitAsync();
        }

        Assert.AreEqual(1, await Db.ApplicationGroups.CountAsync());
    }
}