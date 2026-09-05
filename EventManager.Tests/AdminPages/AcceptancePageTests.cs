using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class AcceptancePageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenApplicationsAreOpen()
    {
        await SetConfigValueAsync(EventStatus.ApplicationsOpen);

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWhenApplicationsAreClosed()
    {
        await SetConfigValueAsync(EventStatus.ApplicationsClosed);

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsForbiddenWhenCheckInStarted()
    {
        await SetConfigValueAsync(EventStatus.CheckInStarted);

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);

        var view = await page.ViewAsync(await GetAdminAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task ModelMatchesDescription()
    {
        {
            AddParticipantGroup(new Participant("alice@example.org") { Status = ParticipantStatus.Finalized });
            AddParticipantGroup(new Participant("bob@example.org") { Status = ParticipantStatus.Created });
            AddParticipantGroup(new Participant("carol@example.org") { Status = ParticipantStatus.Accepted });
            AddParticipantGroup(new Participant("daniel@example.org") { Status = ParticipantStatus.Finalized });
            AddParticipantGroup(new Participant("eve@example.org") { Status = ParticipantStatus.WithdrawnAfterConfirmation });
            AddParticipantGroup(new Participant("fabian@example.org") { Status = ParticipantStatus.Confirmed });
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);
        var modelAsObject = await page.GetModelAsync(await GetAdminAsync());

        var model = Assert.IsInstanceOfType<AcceptancePage.Model>(modelAsObject);
        Assert.AreSequenceEqual(["alice@example.org", "daniel@example.org"], model.FinalizedGroups.SelectMany(g => g.Members).Select(p => p.EmailAddress));
        Assert.AreSequenceEqual(["carol@example.org", "fabian@example.org"], model.AcceptedParticipants.Select(p => p.EmailAddress));
    }

    // These may look odd, but admins know what they're doing, and may want to accept someone even after they've been rejected
    [TestMethod]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Rejected)]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation)]
    [DataRow(ParticipantStatus.DidNotConfirm)]
    public async Task AcceptSpecificAcceptsGroupAndSetsAcceptanceDate(ParticipantStatus status)
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = status },
                new Participant("bob@example.org") { Status = status }
            );
            AddParticipantGroup(
                new Participant("carol@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptSpecificAsync("bob@example.org", "Bob", "Bonobo");
            Assert.AreEqual(Status.Success, result.Status);
            Assert.Contains("bob@example.org", result.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("alice@example.org", result.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("carol@example.org", result.Text, StringComparison.OrdinalIgnoreCase);
            await Db.CommitAsync();
        }

        var newFirst = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(newFirst);
        Assert.AreEqual(ParticipantStatus.Accepted, newFirst.Status);
        Assert.AreEqual(TimeProvider.GetUtcNow(), newFirst.LastStatusReminderDate);

        var newSecond = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newSecond);
        Assert.AreEqual(ParticipantStatus.Accepted, newSecond.Status);
        Assert.AreEqual(TimeProvider.GetUtcNow(), newSecond.LastStatusReminderDate);

        var group = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newFirst));
        Assert.IsNotNull(group);
        Assert.AreEqual(TimeProvider.GetUtcNow(), group.AcceptanceDate);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task AcceptSpecificDoesNotReacceptAlreadyAcceptedGroupMember(ParticipantStatus status)
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = status },
                new Participant("bob@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptSpecificAsync("bob@example.org", "Bob", "Bonobo");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newFirst = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(newFirst);
        Assert.AreEqual(status, newFirst.Status);

        var newSecond = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newSecond);
        Assert.AreEqual(ParticipantStatus.Accepted, newSecond.Status);

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(newSecond.EmailAddress, email.Recipient);
    }

    [TestMethod]
    public async Task AcceptSpecificSendsEmail()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized },
                new Participant("bob@example.org") { Status = ParticipantStatus.Finalized }
            );
            AddParticipantGroup(
                new Participant("carol@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptSpecificAsync("bob@example.org", "Bob", "Bonobo");
            Assert.AreEqual(Status.Success, result.Status);
        }

        Assert.HasCount(2, EmailSender.Outbox);
        Assert.AreSequenceEqual(["alice@example.org", "bob@example.org"], EmailSender.Outbox.Select(e => e.Recipient), SequenceOrder.InAnyOrder);
        var expected = Operation.CreatePageAction<Participant, WaitForAcceptancePage>(nameof(WaitForAcceptancePage.ConfirmAsync));
        Assert.AreEqual(expected, EmailSender.Outbox[0].Operation);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task AcceptSpecificFailsForAlreadyConfirmedParticipant(ParticipantStatus status)
    {
        {
            AddParticipantGroup(new Participant("alice@example.org") { Status = ParticipantStatus.Finalized });
            AddParticipantGroup(new Participant("bob@example.org") { Status = status });
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);
        var result = await page.AcceptSpecificAsync("bob@example.org", "Bob", "Bonobo");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(false, "Apple")]
    [DataRow(false, null)]
    [DataRow(true, "Apple")]
    [DataRow(true, null)]
    public async Task AcceptSpecificSetsStatusAndName(bool exists, string? familyName)
    {
        if (exists)
        {
            var existing = new Participant("alice@example.org") { GivenName = "Alice", FamilyName = null, Status = ParticipantStatus.EmailAddressVerified };
            Db.Participants.Add(existing);
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptSpecificAsync("alice@example.org", "Alice", familyName);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual("Alice", participant.GivenName);
        Assert.AreEqual(familyName, participant.FamilyName);
        Assert.AreEqual(ParticipantStatus.Accepted, participant.Status);
    }

    [TestMethod]
    public async Task AcceptSpecificFailsForSoftRejectedParticipant()
    {
        {
            AddParticipantGroup(new Participant("alice@example.org") { Status = ParticipantStatus.Finalized, IsSoftRejected = true });
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
        var result = await page.AcceptSpecificAsync("alice@example.org", "Alice", "Apple");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task AcceptSpecificFailsForMemberOfGroupIncludingSoftRejectedParticipant()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized },
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    IsSoftRejected = true
                }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
        var result = await page.AcceptSpecificAsync("alice@example.org", "Alice", "Apple");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.Accepted)]
    public async Task RejectSpecificRejectsOnlyIndividual(ParticipantStatus status)
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = status },
                new Participant("bob@example.org") { Status = status }
            );
            AddParticipantGroup(
                new Participant("carol@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.RejectSpecificAsync("alice@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newFirst = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(newFirst);
        Assert.AreEqual(ParticipantStatus.Rejected, newFirst.Status);

        var newFirstGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newFirst));
        Assert.IsNotNull(newFirstGroup);
        Assert.AreSequenceEqual([newFirst], newFirstGroup.Members);

        var newSecond = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newSecond);
        Assert.AreEqual(status, newSecond.Status);

        var newSecondGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newSecond));
        Assert.IsNotNull(newSecondGroup);
        Assert.AreSequenceEqual([newSecond], newSecondGroup.Members);
    }

    [TestMethod]
    public async Task RejectSpecificSendsEmail()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized },
                new Participant("bob@example.org") { Status = ParticipantStatus.Finalized }
            );
            AddParticipantGroup(
                new Participant("carol@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
        var result = await page.RejectSpecificAsync("alice@example.org");

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", email.Recipient);
        Assert.IsNull(email.Operation);
    }

    [TestMethod]
    public async Task RejectSpecificDoesNotYieldOrphanedGroup()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
            await page.RejectSpecificAsync("alice@example.org");
            await Db.CommitAsync();
        }

        Assert.AreEqual(1, await Db.ApplicationGroups.CountAsync());
    }

    [TestMethod]
    public async Task RejectSpecificFailsForUnknownEmail()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
        var result = await page.RejectSpecificAsync("bob@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RejectSpecificFailsForNonFinalizedParticipant()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.ProfileFilled }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
        var result = await page.RejectSpecificAsync("alice@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RejectSpecificFailsForNonFinalizedParticipantWithoutGroup()
    {
        {
            Db.Participants.Add(new Participant("alice@example.org") { Status = ParticipantStatus.ProfileFilled });
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
        var result = await page.RejectSpecificAsync("alice@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Created)]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.CheckedIn)]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation)]
    [DataRow(ParticipantStatus.DidNotConfirm)]
    [DataRow(ParticipantStatus.Rejected)]
    public async Task AcceptOnlyAcceptsFinalized(ParticipantStatus status)
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = status }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, TimeProvider);
        var result = await page.AcceptAsync(1, false, null, true, "");

        Assert.AreEqual(Status.Success, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcceptEarliestPicksEarliest(bool hasSoftRejected)
    {
        Participant MakeParticipant(string email, DateTimeOffset finalizationDate, bool isSoftRejected = false)
        {
            var participant = new Participant(email)
            {
                Status = ParticipantStatus.Finalized,
                IsSoftRejected = isSoftRejected
            };
            var id = "groupOf" + email;
            Db.ApplicationGroups.Add(new(id) { Members = { participant }, FinalizationDate = finalizationDate });
            return participant;
        }

        if (hasSoftRejected)
        {
            Db.Participants.Add(MakeParticipant("softrej@example.org", DateTimeOffset.MinValue, isSoftRejected: true));
        }
        Db.Participants.Add(
            MakeParticipant("alice@example.org", TimeProvider.GetUtcNow().AddDays(-2)),
            MakeParticipant("bob@example.org", TimeProvider.GetUtcNow().AddDays(-1)),
            MakeParticipant("carol@example.org", TimeProvider.GetUtcNow().AddDays(-4)),
            MakeParticipant("daniel@example.org", TimeProvider.GetUtcNow())
        );
        await Db.CommitAsync();

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
        var result = await page.AcceptAsync(2, false, null, true, "");
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreSequenceEqual(["alice@example.org", "carol@example.org"],
                                   await Db.Participants.Where(p => p.Status == ParticipantStatus.Accepted)
                                                        .Select(p => p.EmailAddress)
                                                        .ToCollectionAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcceptHonorsFilter(bool hasSoftRejected)
    {
        {
            if (hasSoftRejected)
            {
                AddParticipantGroup(new Participant("softrej@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "ABC" } }.ToImmutableDictionary(),
                    IsSoftRejected = true
                });
            }
            AddParticipantGroup(
                new Participant("alice@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "A" } }.ToImmutableDictionary()
                }
            );
            AddParticipantGroup(
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "ABC" } }.ToImmutableDictionary()
                }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
        var result = await page.AcceptAsync(1, false, "A", true, "abc");
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreSequenceEqual(["bob@example.org"], await Db.Participants.Where(p => p.Status == ParticipantStatus.Accepted)
                                                                             .Select(p => p.EmailAddress)
                                                                             .ToCollectionAsync());
    }

    [TestMethod]
    public async Task AcceptHonorsInverseFilter()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "ABC" } }.ToImmutableDictionary()
                }
            );
            AddParticipantGroup(
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "A" } }.ToImmutableDictionary()
                }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptAsync(1, false, "A", false, "ABC");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        Assert.AreSequenceEqual(["bob@example.org"], await Db.Participants.Where(p => p.Status == ParticipantStatus.Accepted)
                                                                             .Select(p => p.EmailAddress)
                                                                             .ToCollectionAsync());
    }

    [TestMethod]
    public async Task AcceptIncludesGroupMembersThatDoNotPassTheFilter()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "ABC" } }.ToImmutableDictionary()
                },
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "A" } }.ToImmutableDictionary()
                }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptAsync(1, false, "A", false, "ABC");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newFirst = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(newFirst);
        Assert.AreEqual(ParticipantStatus.Accepted, newFirst.Status);

        var newSecond = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newSecond);
        Assert.AreEqual(ParticipantStatus.Accepted, newSecond.Status);
    }

    [TestMethod]
    public async Task AcceptDoesNotIncludesGroupWithSoftRejectedMember()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "ABC" } }.ToImmutableDictionary()
                },
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "A" } }.ToImmutableDictionary()
                },
                new Participant("softrej@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "A" } }.ToImmutableDictionary(),
                    IsSoftRejected = true
                }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
        var result = await page.AcceptAsync(1, false, "A", false, "ABC");
        Assert.AreEqual(Status.Success, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AcceptHonorsCount(bool random)
    {
        {
            AddParticipantGroup(new Participant("alice@example.org") { Status = ParticipantStatus.Finalized });
            AddParticipantGroup(new Participant("bob@example.org") { Status = ParticipantStatus.Finalized });
            AddParticipantGroup(new Participant("carol@example.org") { Status = ParticipantStatus.Finalized });
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptAsync(1, random, null, true, "");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        Assert.AreEqual(1, await Db.Participants.CountAsync(p => p.Status == ParticipantStatus.Accepted));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AcceptGoesOverDueToGroupsNotUnder(bool random)
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized },
                new Participant("bob@example.org") { Status = ParticipantStatus.Finalized }
            );
            AddParticipantGroup(
                new Participant("carol@example.org") { Status = ParticipantStatus.Created }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptAsync(1, random, null, true, "");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newFirst = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(newFirst);
        Assert.AreEqual(ParticipantStatus.Accepted, newFirst.Status);

        var newSecond = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newSecond);
        Assert.AreEqual(ParticipantStatus.Accepted, newSecond.Status);

        var newThird = await Db.Participants.FindAsync("carol@example.org");
        Assert.IsNotNull(newThird);
        Assert.AreEqual(ParticipantStatus.Created, newThird.Status);
    }

    [TestMethod]
    public async Task AcceptRandomLooksRandom()
    {
        const int count = 1_000;
        {
            foreach (int n in Enumerable.Range(1, count))
            {
                AddParticipantGroup(
                    new Participant(n.ToString(CultureInfo.InvariantCulture) + "@example.org") { Status = ParticipantStatus.Finalized }
                );
            }
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
            var result = await page.AcceptAsync(3, true, null, true, "");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var topThree = await Db.Participants.Take(3).ToCollectionAsync();
        Assert.IsFalse(topThree.All(p => p.Status is ParticipantStatus.Accepted));

        var bottomThree = await Db.Participants.Skip(count - 3).ToCollectionAsync();
        Assert.IsFalse(bottomThree.All(p => p.Status is ParticipantStatus.Accepted));
    }

    [TestMethod]
    public async Task AcceptDoesNotAcceptDuplicates()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized },
                new Participant("bob@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
        await page.AcceptAsync(3, false, null, true, "");

        Assert.HasCount(2, EmailSender.Outbox);
    }

    [TestMethod]
    public async Task AcceptWithInvertedFilterFailsIfFilterDoesNotMatchAnyone()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "XYZ" } }.ToImmutableDictionary()
                }
            );
            AddParticipantGroup(
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.Finalized,
                    Profile = new Dictionary<string, string> { { "A", "A" } }.ToImmutableDictionary()
                }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, TimeProvider);
        var result = await page.AcceptAsync(1, false, "A", false, "ABC");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AcceptSendsEmail(bool random)
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, TimeProvider);
        var result = await page.AcceptAsync(1, random, null, true, "");

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", email.Recipient);
        // Avoids confusion when the same org has multiple events around the same time
        Assert.Contains(EventDetails.ToString(), email.Body, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task AcceptFailsIfAttributeIsSetButEqualityIsNot()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
        var result = await page.AcceptAsync(1, false, "attr", null, "value");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task AcceptFailsIfAttributeIsSetButValueIsNot()
    {
        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
        var result = await page.AcceptAsync(1, false, "attr", true, null);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Created)]
    [DataRow(ParticipantStatus.Finalized)]
    public async Task CloseRejectsNonAccepted(ParticipantStatus status)
    {
        await SetConfigValueAsync(EventStatus.ApplicationsOpen);

        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = status }
            );
            await Db.CommitAsync();
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
            var result = await page.CloseAsync(await GetAdminAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(participant);
        Assert.AreEqual(ParticipantStatus.Rejected, participant.Status);
    }

    [TestMethod]
    public async Task CloseSendsEmails()
    {
        await SetConfigValueAsync(EventStatus.ApplicationsOpen);

        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = ParticipantStatus.Finalized }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, EmailSender, DisabledTimeProvider);
        var result = await page.CloseAsync(await GetAdminAsync());

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice@example.org", email.Recipient);
        // Avoids confusion when the same org has multiple events around the same time
        Assert.Contains(EventDetails.ToString(), email.Body, StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation)]
    [DataRow(ParticipantStatus.Rejected)]
    [DataRow(ParticipantStatus.DidNotConfirm)]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task CloseDoesNotChangeIrrelevantParticipants(ParticipantStatus status)
    {
        await SetConfigValueAsync(EventStatus.ApplicationsOpen);

        {
            AddParticipantGroup(
                new Participant("alice@example.org") { Status = status }
            );
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);
        var result = await page.CloseAsync(await GetAdminAsync());
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task CloseSetsStatusToApplicationsClosed()
    {
        await SetConfigValueAsync(EventStatus.ApplicationsOpen);

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);

        var result = await page.CloseAsync(await GetAdminAsync());
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreEqual(EventStatus.ApplicationsClosed, config.EventStatus);
    }

    [TestMethod]
    public async Task CloseFailsIfStatusIsNotApplicationsOpen()
    {
        await SetConfigValueAsync(EventStatus.ApplicationsClosed);

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);

        var result = await page.CloseAsync(await GetAdminAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CloseFailsIfAdminIsNotOwner()
    {
        await SetConfigValueAsync(EventStatus.ApplicationsOpen);

        var notOwner = await CreateNonOwnerAdminAsync();

        var config = await Config.CreateAsync(Db);
        var page = new AcceptancePage(Db.Participants, Db.ApplicationGroups, new ConfigValue<EventStatus>(config), EventDetails, DisabledEmailSender, DisabledTimeProvider);

        var result = await page.CloseAsync(notOwner);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    private void AddParticipantGroup(params IReadOnlyCollection<Participant> participants)
    {
        var id = string.Join(';', participants.Select(p => p.EmailAddress));
        var group = new ApplicationGroup(id);
        foreach (var participant in participants)
        {
            Db.Participants.Add(participant);
            group.Members.Add(participant);
        }
        Db.ApplicationGroups.Add(group);
    }
}