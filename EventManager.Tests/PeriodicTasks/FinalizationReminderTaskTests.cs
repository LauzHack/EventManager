using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.PeriodicTasks;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.PeriodicTasks;

[TestClass]
public sealed class FinalizationReminderTaskTests : TestsBase
{
    [TestMethod]
    public async Task TaskDoesNothingWhenNobodyHasProfileFilled()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org") { Status = ParticipantStatus.Created },
                new Participant("bob@example.org") { Status = ParticipantStatus.Created }
            );
            await Db.CommitAsync();
        }

        await new FinalizationReminderTask(Db.Participants, EventLimits, EmailSender, TimeProvider).RunAsync();

        Assert.IsFalse(await Db.CommitAsync());
        Assert.IsEmpty(EmailSender.Outbox);
    }

    [TestMethod]
    public async Task TaskSendsEmailReminderToParticipantsAfterEachPeriod()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled,
                    LastStatusReminderDate = TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders) / 2
                },
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled,
                    LastStatusReminderDate = TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders)
                },
                new Participant("carol@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled,
                    LastStatusReminderDate = TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders) * 5
                },
                new Participant("daniel@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled
                },
                new Participant("eve@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled,
                    LastStatusReminderDate = TimeProvider.GetUtcNow() + TimeSpan.FromHours(1) // DST shenanigans
                },
                new Participant("fares@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled,
                    LastStatusReminderDate = TimeProvider.GetUtcNow()
                }
            );
            await Db.CommitAsync();
        }

        await new FinalizationReminderTask(Db.Participants, EventLimits, EmailSender, TimeProvider).RunAsync();

        Assert.HasCount(3, EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", EmailSender.Outbox[0].Recipient);
        Assert.AreEqual("carol@example.org", EmailSender.Outbox[1].Recipient);
        Assert.AreEqual("daniel@example.org", EmailSender.Outbox[2].Recipient);
        Assert.AreEqual(Operation.CreatePageView<Participant>(), EmailSender.Outbox[0].Operation);
    }

    [TestMethod]
    public async Task TaskSetsLastStatusReminderDateWhenReminding()
    {
        {
            Db.Participants.Add(
                new Participant("alice@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled,
                    LastStatusReminderDate = TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders) / 2
                },
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled,
                    LastStatusReminderDate = TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders)
                },
                new Participant("carol@example.org")
                {
                    Status = ParticipantStatus.ProfileFilled,
                    LastStatusReminderDate = TimeProvider.GetUtcNow() - TimeSpan.FromDays(EventLimits.DaysBetweenReminders) * 5
                }
            );
            await Db.CommitAsync();
        }

        IReadOnlyCollection<Participant> originalParticipants;
        {
            originalParticipants = await Db.Participants.ToCollectionAsync();
            await new FinalizationReminderTask(Db.Participants, EventLimits, EmailSender, TimeProvider).RunAsync();
            await Db.CommitAsync();
        }

        var participants = await Db.Participants.ToCollectionAsync();
        Assert.AreEqual(originalParticipants.ElementAt(0).LastStatusReminderDate, participants.ElementAt(0).LastStatusReminderDate);
        Assert.AreEqual(TimeProvider.GetUtcNow(), participants.ElementAt(1).LastStatusReminderDate);
        Assert.AreEqual(participants.ElementAt(1).LastStatusReminderDate, participants.ElementAt(2).LastStatusReminderDate);
    }
}