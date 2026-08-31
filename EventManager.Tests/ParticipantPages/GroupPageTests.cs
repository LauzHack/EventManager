using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class GroupPageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenNotFinalized()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.ProfileFilled;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var view = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(0u)]
    [DataRow(1u)]
    public async Task PageTitleDoesNotMentionGroupWhenSizeLimitIsOneOrZero(uint size)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.ProfileFilled;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var page = new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits with { ApplicationGroupSize = size }, EventDetails, DisabledEmailSender, DisabledTimeProvider);
        var view = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
        Assert.DoesNotContain("group", view.Title, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task PageIsSummaryOnlyWhenFinalized()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var view = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsNotEmpty(view.Summary);
    }

    [TestMethod]
    [DataRow(0u)]
    [DataRow(1u)]
    public async Task PageIsHiddenWhenFinalizedIfGroupSizeIsOneOrZero(uint size)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var view = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits with { ApplicationGroupSize = size }, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task PageIsHiddenOnceAccepted(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var view = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    public async Task ModelHasInvitedGroups()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.ProfileFilled;
            var participant0 = new Participant("bob@example.org") { Status = ParticipantStatus.ProfileFilled };
            var participant1 = new Participant("carol@example.org") { Status = ParticipantStatus.ProfileFilled };
            var participant2 = new Participant("daniel@example.org");
            Db.Participants.Add(participant0, participant1, participant2);
            Db.ApplicationGroups.Add(
                new("id1") { Members = { participant } },
                new("id2") { Members = { participant0 }, InvitedParticipants = { participant, participant2 } },
                new("id3") { Members = { participant1 }, InvitedParticipants = { participant0 } }
            );
            await Db.CommitAsync();
        }

        var inviter = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(inviter);
        var inviterGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(inviter));
        Assert.IsNotNull(inviterGroup);

        var modelAsObject = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .GetModelAsync(await GetParticipantAsync());
        var model = Assert.IsInstanceOfType<GroupPage.Model>(modelAsObject);

        Assert.AreSequenceEqual([inviterGroup], model.InvitedGroups);
    }

    [TestMethod]
    public async Task ModelHasGroup()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.ProfileFilled;
            var other = new Participant("example@example.org");
            Db.Participants.Add(other);
            Db.ApplicationGroups.Add(
                new("id") { Members = { participant }, InvitedParticipants = { other } }
            );
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNotNull(newGroup);

        var modelAsObject = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .GetModelAsync(await GetParticipantAsync());
        var model = Assert.IsInstanceOfType<GroupPage.Model>(modelAsObject);

        Assert.AreEqual(newGroup, model.Group);
    }

    [TestMethod]
    public async Task SummaryIsNotEmptyWhenAlone()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var view = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .ViewAsync(await GetParticipantAsync());

        Assert.HasCount(1, view.Summary);
    }

    [TestMethod]
    public async Task SummaryIsGroupOtherMemberNamesWhenNotAlone()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Finalized;
            var bob = new Participant("bob@example.org")
            {
                GivenName = "Bob",
                FamilyName = "Bonobo",
                Status = ParticipantStatus.Finalized
            };
            var carol = new Participant("carol@example.org")
            {
                GivenName = "Carol",
                Status = ParticipantStatus.Finalized
            };
            Db.Participants.Add(bob, carol);
            Db.ApplicationGroups.Add(new("id") { Members = { participant, bob, carol } });
            await Db.CommitAsync();
        }

        var view = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .ViewAsync(await GetParticipantAsync());

        Assert.AreSequenceEqual([("Applying with", "Bob Bonobo, Carol")], view.Summary);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CreateInviteAddsInvite(bool alreadyExists)
    {
        {
            var participant = await GetParticipantAsync();
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            if (alreadyExists)
            {
                AddParticipantGroup(new Participant("bob@example.org") { Status = ParticipantStatus.Created });
            }
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
            .CreateInvitationAsync(await GetParticipantAsync(), "bob@example.org");
        await Db.CommitAsync();

        Assert.AreEqual(Status.Success, result.Status);
        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNotNull(newGroup);
        Assert.AreSequenceEqual(["bob@example.org"], newGroup.InvitedParticipants.Select(p => p.EmailAddress));
    }

    [TestMethod]
    public async Task CreateInviteSendsEmailToInvitedParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
            .CreateInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", email.Recipient);
        // Ensure the person has some context
        Assert.Contains(EventDetails.ToString(), email.Body, StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow("bob@example.org", DisplayName = "Same case")]
    [DataRow("bOb@eXamPLe.Org", DisplayName = "Different case")]
    public async Task CreateInviteDoesNotDuplicateIfItAlreadyExists(string emailAddressToInvite)
    {
        {
            var participant = await GetParticipantAsync();
            var other = new Participant("bob@example.org");
            Db.Participants.Add(other);
            Db.ApplicationGroups.Add(new("id") { Members = { participant }, InvitedParticipants = { other } });
            await Db.CommitAsync();
        }

        {
            var page = new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider);
            var result = await page.CreateInvitationAsync(await GetParticipantAsync(), emailAddressToInvite);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        var group = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNotNull(group);
        Assert.AreSequenceEqual(["bob@example.org"], group.InvitedParticipants.Select(p => p.EmailAddress));
    }

    [TestMethod]
    public async Task CreateInviteSendsEmailEvenIfInviteAlreadyExists()
    {
        {
            var participant = await GetParticipantAsync();
            var other = new Participant("bob@example.org");
            Db.Participants.Add(other);
            Db.ApplicationGroups.Add(new("id") { Members = { participant }, InvitedParticipants = { other } });
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
            .CreateInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", email.Recipient);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CannotInviteYourself(bool toUpper)
    {
        {
            var participant = await GetParticipantAsync();
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .CreateInvitationAsync(await GetParticipantAsync(), toUpper ? ParticipantEmailAddress.ToUpperInvariant() : ParticipantEmailAddress);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation)]
    [DataRow(ParticipantStatus.DidNotConfirm)]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Accepted)]
    public async Task CannotInviteParticipantWhoAlreadyFinishedTheProcess(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            AddParticipantGroup(new Participant("bob@example.org") { Status = status });
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
            .CreateInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CreateInviteFailsWhenGroupHasMaxSize()
    {
        {
            var participant = await GetParticipantAsync();
            var group = new ApplicationGroup("id") { Members = { participant } };
            for (int n = 0; n < EventLimits.ApplicationGroupSize - 1; n++)
            {
                var p = new Participant(n.ToString(CultureInfo.InvariantCulture) + "@example.org");
                group.Members.Add(p);
                Db.Participants.Add(p);
            }
            Db.ApplicationGroups.Add(group);
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .CreateInvitationAsync(await GetParticipantAsync(), "another@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CreateInviteFailsWhenGroupHasMaxInvitationsAndMembers()
    {
        {
            var participant = await GetParticipantAsync();
            var bob = new Participant("bob@example.org");
            var group = new ApplicationGroup("id") { Members = { participant, bob } };
            Db.Participants.Add(bob);
            for (int n = 0; n < EventLimits.ApplicationGroupSize - 2; n++)
            {
                var p = new Participant(n.ToString(CultureInfo.InvariantCulture) + "@example.org");
                Db.Participants.Add(p);
                group.InvitedParticipants.Add(p);
            }
            Db.ApplicationGroups.Add(group);
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .CreateInvitationAsync(await GetParticipantAsync(), "another@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow("carol@example.org", DisplayName = "Same case")]
    [DataRow("cARol@eXamPLe.Org", DisplayName = "Different case")]
    public async Task CancelInviteCancelsInvite(string emailAddressToCancel)
    {
        {
            var participant = await GetParticipantAsync();
            var other0 = new Participant("bob@example.org");
            var other1 = new Participant("carol@example.org");
            Db.Participants.Add(other0, other1);
            Db.ApplicationGroups.Add(new("id") { Members = { participant }, InvitedParticipants = { other0, other1 } });
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
                .CancelInvitationAsync(await GetParticipantAsync(), emailAddressToCancel);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNotNull(newGroup);
        Assert.AreSequenceEqual(["bob@example.org"], newGroup.InvitedParticipants.Select(p => p.EmailAddress));
    }

    [TestMethod]
    public async Task CancelInviteSendsEmailToInvitedParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            var other = new Participant("bob@example.org");
            Db.Participants.Add(other);
            Db.ApplicationGroups.Add(new("id") { Members = { participant }, InvitedParticipants = { other } });
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
            .CancelInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("bob@example.org", email.Recipient);
        Assert.IsNull(email.Operation);
        // Ensure the person has some context
        Assert.Contains(EventDetails.ToString(), email.Body, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CancelInviteDoesNothingForNonexistentInvite()
    {
        {
            var participant = await GetParticipantAsync();
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .CancelInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        // See the method implementation for why we expect Success and not None
        Assert.AreEqual(Status.Success, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task CancelInviteFailsIfParticipantIsNotInAGroup()
    {
        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .CancelInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcceptInviteJoinsGroup(bool hadGroup)
    {
        if (hadGroup)
        {
            var participant = await GetParticipantAsync();
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
        }

        {
            var participant = await GetParticipantAsync();
            var other = new Participant("bob@example.org") { Status = ParticipantStatus.ProfileFilled };
            Db.Participants.Add(other);
            Db.ApplicationGroups.Add(new("other") { Members = { other }, InvitedParticipants = { participant } });
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
                .AcceptInvitationAsync(await GetParticipantAsync(), "other");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNotNull(newGroup);
        var newOtherGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newOther));
        Assert.AreEqual(newOtherGroup, newGroup);

        var emptyGroups = await Db.ApplicationGroups.Where(g => g.Members.Count == 0).ToCollectionAsync();
        Assert.IsEmpty(emptyGroups);
    }

    [TestMethod]
    public async Task AcceptInviteRemovesInvite()
    {
        {
            var participant = await GetParticipantAsync();
            var other0 = new Participant("bob@example.org") { Status = ParticipantStatus.ProfileFilled };
            var other1 = new Participant("carol@example.org");
            Db.Participants.Add(other0, other1);
            Db.ApplicationGroups.Add(new("id") { Members = { other0 }, InvitedParticipants = { participant, other1 } });
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
                .AcceptInvitationAsync(await GetParticipantAsync(), "id");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newOther));
        Assert.IsNotNull(newGroup);
        Assert.AreSequenceEqual(["carol@example.org"], newGroup.InvitedParticipants.Select(p => p.EmailAddress));
    }

    [TestMethod]
    public async Task AcceptInviteFailsForNonexistentGroup()
    {
        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .AcceptInvitationAsync(await GetParticipantAsync(), "doesnotexist");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task AcceptInviteFailsWithoutInvite()
    {
        {
            var other = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.ProfileFilled
            };
            var otherGroup = new ApplicationGroup("id") { Members = { other } };
            Db.Participants.Add(other);
            Db.ApplicationGroups.Add(otherGroup);
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .AcceptInvitationAsync(await GetParticipantAsync(), "id");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RejectInviteRejectsInvite()
    {
        {
            var participant = await GetParticipantAsync();
            var other = new Participant("bob@example.org") { Status = ParticipantStatus.ProfileFilled };
            Db.Participants.Add(other);
            Db.ApplicationGroups.Add(new("other") { Members = { other }, InvitedParticipants = { participant } });
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
                .RejectInvitationAsync(await GetParticipantAsync(), "other");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNull(newGroup);
        var newOtherGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newOther));
        Assert.IsNotNull(newOtherGroup);
        Assert.AreNotEqual(newOtherGroup, newGroup);
        Assert.IsEmpty(newOtherGroup.InvitedParticipants);
    }

    [TestMethod]
    public async Task RejectInviteSucceedsEvenForMissingGroup()
    {
        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .RejectInvitationAsync(await GetParticipantAsync(), "doesnotexist");
        Assert.AreEqual(Status.Success, result.Status);
    }

    [TestMethod]
    [DataRow("bob@example.org", DisplayName = "Same case")]
    [DataRow("BoB@EXAMple.ORg", DisplayName = "Different case")]
    public async Task RemoveMemberRemovesMember(string emailToRemove)
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.ProfileFilled
            };
            Db.Participants.Add(bob);
            Db.ApplicationGroups.Add(new("id") { Members = { participant, bob } });
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
                .RemoveMemberAsync(await GetParticipantAsync(), emailToRemove);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNotNull(newGroup);
        Assert.AreSequenceEqual([newParticipant], newGroup.Members);

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newOtherGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newOther));
        Assert.IsNull(newOtherGroup);

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(newOther.EmailAddress, email.Recipient, StringComparer.Ordinal);
        Assert.IsNotNull(newParticipant.FullName);
        Assert.Contains(newParticipant.FullName, email.Body, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RemoveMemberReturnsErrorIfEmailAddressWasNotMember()
    {
        {
            var participant = await GetParticipantAsync();
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.ProfileFilled
            };
            Db.Participants.Add(bob);
            Db.ApplicationGroups.Add(new("id") { Members = { participant, bob } });
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .RemoveMemberAsync(await GetParticipantAsync(), "carol@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RemoveMemberFailsIfParticipantIsNotInAGroup()
    {
        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
            .RemoveMemberAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow("participant@example.org", DisplayName = "Same case")]
    [DataRow("pARTiciPaNt@EXAMple.ORg", DisplayName = "Different case")]
    public async Task RemoveMemberLeavesGroupWhenRemovingOneself(string emailToRemove)
    {
        {
            var participant = await GetParticipantAsync();
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.ProfileFilled
            };
            Db.Participants.Add(bob);
            Db.ApplicationGroups.Add(new("id") { Members = { participant, bob } });
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
                .RemoveMemberAsync(await GetParticipantAsync(), emailToRemove);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }


        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNull(newGroup);

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newOtherGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newOther));
        Assert.IsNotNull(newOtherGroup);
        Assert.AreSequenceEqual([newOther], newOtherGroup.Members);
    }

    [TestMethod]
    public async Task RemoveSelfDoesNotYieldOrphanedGroup()
    {
        {
            var participant = await GetParticipantAsync();
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, DisabledTimeProvider)
                .RemoveMemberAsync(await GetParticipantAsync(), ParticipantEmailAddress);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNull(newGroup);
    }

    [TestMethod]
    public async Task FinalizeFailsIfThereArePendingInvitationsFromParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            var other = new Participant("bob@example.org");
            Db.Participants.Add(other);
            Db.ApplicationGroups.Add(new("id") { Members = { participant }, InvitedParticipants = { other } });
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
            .FinalizeAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task FinalizeFailsIfThereArePendingInvitationsToParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            var otherParticipant = new Participant("bob@example.org");
            Db.Participants.Add(otherParticipant);
            Db.ApplicationGroups.Add(
                new("id") { Members = { participant } },
                new("other") { Members = { otherParticipant }, InvitedParticipants = { participant } }
            );
            await Db.CommitAsync();
        }

        var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, DisabledTimeProvider)
            .FinalizeAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task FinalizeMakesAllGroupMembersFinalizedAndSetsFinalizationDate()
    {
        {
            var participant = await GetParticipantAsync();
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.ProfileFilled
            };
            AddParticipantGroup(new Participant("carol@example.org")
            {
                Status = ParticipantStatus.ProfileFilled
            });
            Db.Participants.Add(bob);
            Db.ApplicationGroups.Add(new("id") { Members = { participant, bob } });
            await Db.CommitAsync();
        }

        {
            var page = new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, TimeProvider);
            var result = await page.FinalizeAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual(ParticipantStatus.Finalized, newParticipant.Status);

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        Assert.AreEqual(ParticipantStatus.Finalized, newOther.Status);

        var newUntouched = await Db.Participants.FindAsync("carol@example.org");
        Assert.IsNotNull(newUntouched);
        Assert.AreEqual(ParticipantStatus.ProfileFilled, newUntouched.Status);

        var newGroup = await Db.ApplicationGroups.FirstOrDefaultAsync(g => g.Members.Contains(newParticipant));
        Assert.IsNotNull(newGroup);
        Assert.AreEqual(TimeProvider.GetUtcNow(), newGroup.FinalizationDate);
    }

    [TestMethod]
    public async Task FinalizeSendsEmailsWithAloneMentionWhenAlone()
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, TimeProvider)
                .FinalizeAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
        }

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, email.Recipient);
        Assert.Contains("on your own", email.Body, StringComparison.Ordinal);
        Assert.IsNull(email.Operation);
        // it's important we don't invite people officially yet, this is just finalization not acceptance
        Assert.IsNull(email.AttachedEvent);
    }

    [TestMethod]
    [DataRow(0u)]
    [DataRow(1u)]
    public async Task FinalizeSendsEmailsWithoutAloneMentionWhenGroupLimitIsOneOrZero(uint size)
    {
        {
            var participant = await GetParticipantAsync();
            Db.ApplicationGroups.Add(new("id") { Members = { participant } });
            await Db.CommitAsync();
        }

        {
            var page = new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits with { ApplicationGroupSize = size }, EventDetails, EmailSender, TimeProvider);
            var result = await page.FinalizeAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
        }

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, email.Recipient);
        Assert.DoesNotContain("on your own", email.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("with", email.Body, StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinalizeSendsEmailsToGroupMembersIncludingNames(bool multiple)
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            var bob = new Participant("bob@example.org")
            {
                GivenName = "Bob",
                FamilyName = "Banana",
                Status = ParticipantStatus.ProfileFilled
            };
            Db.Participants.Add(bob);
            var group = new ApplicationGroup("id") { Members = { participant, bob } };
            if (multiple)
            {
                var carol = new Participant("carol@example.org")
                {
                    GivenName = "Carol",
                    FamilyName = "Coconut",
                    Status = ParticipantStatus.ProfileFilled
                };
                group.Members.Add(carol);
                Db.Participants.Add(carol);
            }
            Db.ApplicationGroups.Add(group);
            await Db.CommitAsync();
        }

        {
            var result = await new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, TimeProvider)
                .FinalizeAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
        }

        Assert.HasCount(multiple ? 3 : 2, EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, EmailSender.Outbox[0].Recipient);
        Assert.Contains("Bob Banana", EmailSender.Outbox[0].Body, StringComparison.Ordinal);
        if (multiple)
        {
            Assert.Contains("Carol Coconut", EmailSender.Outbox[0].Body, StringComparison.Ordinal);
        }
        Assert.AreEqual("bob@example.org", EmailSender.Outbox[1].Recipient);
        Assert.Contains("Alice Apple", EmailSender.Outbox[1].Body, StringComparison.Ordinal);
        Assert.IsNull(EmailSender.Outbox[0].Operation);
        // it's important we don't invite people officially yet, this is just finalization not acceptance
        Assert.IsNull(EmailSender.Outbox[0].AttachedEvent);
    }

    [TestMethod]
    public async Task WithdrawWithdrawsAndSendsEmail()
    {
        {
            var participant = await GetParticipantAsync();
            var other = new Participant("bob@example.org");
            Db.Participants.Add(other);
            var group = new ApplicationGroup("id") { Members = { participant, other } };
            Db.ApplicationGroups.Add(group);
            await Db.CommitAsync();
        }

        var page = new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, TimeProvider);
        var result = await page.WithdrawAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, email.Recipient, StringComparer.Ordinal);

        var newGroup = Assert.ContainsSingle(await Db.ApplicationGroups.ToCollectionAsync());
        var newOther = Assert.ContainsSingle(newGroup.Members);
        Assert.AreEqual("bob@example.org", newOther.EmailAddress, StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task WithdrawDoesNotLeaveOrphanedGroup()
    {
        {
            var participant = await GetParticipantAsync();
            var group = new ApplicationGroup("id") { Members = { participant } };
            Db.ApplicationGroups.Add(group);
            await Db.CommitAsync();
        }

        var page = new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, EmailSender, TimeProvider);
        var result = await page.WithdrawAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var groups = await Db.ApplicationGroups.ToCollectionAsync();
        Assert.IsEmpty(groups);
    }

    [TestMethod]
    public async Task WithdrawDoesNothingIfAlreadyWithdrawn()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.WithdrawnBeforeConfirmation;
            await Db.CommitAsync();
        }

        var page = new GroupPage(Db.Participants, Db.ApplicationGroups, EventLimits, EventDetails, DisabledEmailSender, TimeProvider);
        var result = await page.WithdrawAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.None, result.Status);
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