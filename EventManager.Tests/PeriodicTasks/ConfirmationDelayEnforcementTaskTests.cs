using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.PeriodicTasks;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.PeriodicTasks;

[TestClass]
public sealed class ConfirmationDelayEnforcementTaskTests : TestsBase
{
    [TestMethod]
    public async Task TaskDoesNothingWhenNobodyIsAccepted()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org"),
                new Participant("bob@example.org") { Status = ParticipantStatus.Finalized },
                new Participant("carol@example.org") { Status = ParticipantStatus.Confirmed }
            );
            await Db.CommitAsync();
        }

        await new ConfirmationDelayEnforcementTask(Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, TimeProvider).RunAsync();

        Assert.IsFalse(await Db.CommitAsync());
        Assert.IsEmpty(EmailSender.Outbox);
    }

    [TestMethod]
    public async Task TaskSetsStatusToDidNotConfirmAfterLimit()
    {
        {
            var alice = new Participant("alice@example.org") { Status = ParticipantStatus.Accepted };
            var bob = new Participant("bob@example.org") { Status = ParticipantStatus.Accepted };
            Db.Participants.Add(alice, bob);
            Db.ApplicationGroups.Add(
                new("a") { Members = { alice }, AcceptanceDate = TimeProvider.GetUtcNow() },
                new("b") { Members = { bob }, AcceptanceDate = TimeProvider.GetUtcNow().AddDays(-EventLimits.DaysToConfirm).AddSeconds(-1) }
            );
            await Db.CommitAsync();
        }

        {
            await new ConfirmationDelayEnforcementTask(Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, TimeProvider).RunAsync();
            await Db.CommitAsync();
        }

        var participants = await Db.Participants.ToCollectionAsync();
        Assert.AreEqual(ParticipantStatus.Accepted, participants.ElementAt(0).Status);
        Assert.AreEqual(ParticipantStatus.DidNotConfirm, participants.ElementAt(1).Status);
    }

    [TestMethod]
    public async Task TaskSendsEmailAfterLimit()
    {
        {
            var alice = new Participant("alice@example.org") { Status = ParticipantStatus.Accepted };
            var bob = new Participant("bob@example.org") { Status = ParticipantStatus.Accepted };
            Db.Participants.Add(alice, bob);
            Db.ApplicationGroups.Add(
                new("a") { Members = { alice }, AcceptanceDate = TimeProvider.GetUtcNow() },
                new("b") { Members = { bob }, AcceptanceDate = TimeProvider.GetUtcNow().AddDays(-EventLimits.DaysToConfirm).AddSeconds(-1) }
            );
            await Db.CommitAsync();
        }

        await new ConfirmationDelayEnforcementTask(Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, TimeProvider).RunAsync();

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", email.Recipient);
        Assert.IsNull(email.Operation);
        // ensure the participant isn't confused as to which event they got rejected from
        Assert.Contains(EventDetails.ToString(), email.Body, StringComparison.Ordinal);
    }
}