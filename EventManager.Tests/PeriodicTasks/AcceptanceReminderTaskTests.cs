using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.PeriodicTasks;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.PeriodicTasks;

[TestClass]
public sealed class AcceptanceReminderTaskTests : TestsBase
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

        await new AcceptanceReminderTask(Db.ApplicationGroups, EventLimits, EmailSender, TimeProvider).RunAsync();

        Assert.IsFalse(await Db.CommitAsync());
        Assert.IsEmpty(EmailSender.Outbox);
    }

    [TestMethod]
    public async Task TaskSendsEmailReminderToParticipantsAfterEachPeriod()
    {
        Participant MakeParticipant(string email, DateTimeOffset? lastStatusReminderDate)
        {
            var participant = new Participant(email)
            {
                Status = ParticipantStatus.Accepted,
                LastStatusReminderDate = lastStatusReminderDate
            };
            var id = "groupOf" + participant.EmailAddress;
            Db.ApplicationGroups.Add(new(id) { Members = { participant }, AcceptanceDate = DateTimeOffset.MinValue });
            return participant;
        }

        {
            Db.Participants.Add(
                MakeParticipant("alice@example.org", TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders) / 2),
                MakeParticipant("bob@example.org", TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders)),
                MakeParticipant("carol@example.org", TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders) * 5),
                MakeParticipant("daniel@example.org", null),
                MakeParticipant("eve@example.org", TimeProvider.GetUtcNow() + TimeSpan.FromHours(1)), // DST shenanigans
                MakeParticipant("fares@example.org", TimeProvider.GetUtcNow())
            );
            await Db.CommitAsync();
        }

        await new AcceptanceReminderTask(Db.ApplicationGroups, EventLimits, EmailSender, TimeProvider).RunAsync();

        Assert.HasCount(3, EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", EmailSender.Outbox[0].Recipient);
        Assert.AreEqual("carol@example.org", EmailSender.Outbox[1].Recipient);
        Assert.AreEqual("daniel@example.org", EmailSender.Outbox[2].Recipient);
        Assert.AreEqual(Operation.CreatePageAction<Participant, WaitForAcceptancePage>(nameof(WaitForAcceptancePage.ConfirmAsync)), EmailSender.Outbox[0].Operation);
    }

    [TestMethod]
    public async Task TaskSetsLastStatusReminderDateWhenReminding()
    {
        Participant MakeParticipant(string email, DateTimeOffset? lastStatusReminderDate)
        {
            var participant = new Participant(email)
            {
                Status = ParticipantStatus.Accepted,
                LastStatusReminderDate = lastStatusReminderDate
            };
            var id = "groupOf" + participant.EmailAddress;
            Db.ApplicationGroups.Add(new(id) { Members = { participant }, AcceptanceDate = DateTimeOffset.MinValue });
            return participant;
        }

        {
            Db.Participants.Add(
                MakeParticipant("alice@example.org", TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders) / 2),
                MakeParticipant("bob@example.org", TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders)),
                MakeParticipant("carol@example.org", TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders) * 5),
                MakeParticipant("daniel@example.org", null)
            );
            await Db.CommitAsync();
        }

        IReadOnlyCollection<Participant> originalParticipants;
        {
            originalParticipants = await Db.Participants.ToCollectionAsync();
            await new AcceptanceReminderTask(Db.ApplicationGroups, EventLimits, EmailSender, TimeProvider).RunAsync();
            await Db.CommitAsync();
        }

        var participants = await Db.Participants.ToCollectionAsync();
        Assert.AreEqual(originalParticipants.ElementAt(0).LastStatusReminderDate, participants.ElementAt(0).LastStatusReminderDate);
        Assert.AreEqual(TimeProvider.GetUtcNow(), participants.ElementAt(1).LastStatusReminderDate);
        Assert.AreEqual(participants.ElementAt(1).LastStatusReminderDate, participants.ElementAt(2).LastStatusReminderDate);
        Assert.AreEqual(participants.ElementAt(2).LastStatusReminderDate, participants.ElementAt(3).LastStatusReminderDate);
    }
}