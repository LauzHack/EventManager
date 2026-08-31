using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class AliasPageTests : ParticipantTestsBase
{
    [TestMethod]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task PageIsHiddenWhenAlreadyAccepted(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            participant.Status = status;
            Db.Participants.Add(
                new Participant("alice2@example.org") { GivenName = "Alice", FamilyName = "Apple" }
            );
            await Db.CommitAsync();
        }

        var view = await new AliasPage(DisabledEmailSender).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    public async Task PageIsHiddenWhenNoPossibleAliases()
    {
        var view = await new AliasPage(DisabledEmailSender).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    public async Task PageIsRequiredWhenThereArePossibleAliases()
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            var other = new Participant("other@example.org")
            {
                GivenName = "aLiCe",
                FamilyName = "ApPlE",
                Status = ParticipantStatus.ProfileFilled
            };
            participant.PossibleAliasEmailAddresses = ["other@example.org"];
            Db.Participants.Add(other);
            await Db.CommitAsync();
        }

        var view = await new AliasPage(DisabledEmailSender).ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsStillRequiredIfParticipantTriesWrongEmail()
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            var other = new Participant("alice2@example.org")
            {
                GivenName = "Alice",
                FamilyName = "Apple"
            };
            participant.PossibleAliasEmailAddresses = ["alice2@example.org"];
            Db.Participants.Add(other);
            await Db.CommitAsync();
        }

        {
            await new AliasPage(DisabledEmailSender).ViewAsync(await GetParticipantAsync());
            await Db.CommitAsync();
        }

        {
            var result = await new AliasPage(DisabledEmailSender).ChooseCandidateAsync(await GetParticipantAsync(), "alice12345@example.org");
            Assert.AreEqual(Status.UserError, result.Status);
            await Db.CommitAsync();
        }

        var view2 = await new AliasPage(DisabledEmailSender).ViewAsync(await GetParticipantAsync());
        Assert.IsTrue(view2.IsRequired);
        Assert.IsTrue(view2.IsInteractable);
    }

    [TestMethod]
    public async Task ChooseSendsEmailToCandidateSelectedByParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            var other = new Participant("alice2@example.org")
            {
                GivenName = "Alice",
                FamilyName = "Apple"
            };
            participant.PossibleAliasEmailAddresses = ["alice2@example.org"];
            Db.Participants.Add(other);
            await Db.CommitAsync();
        }

        var result = await new AliasPage(EmailSender).ChooseCandidateAsync(await GetParticipantAsync(), "alice2@example.org");
        Assert.AreEqual(Status.ImportantInformation, result.Status);
        await Db.CommitAsync();

        var participant2 = await GetParticipantAsync();
        Assert.AreEqual("alice2@example.org", participant2.FutureEmailAddress);

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("alice2@example.org", email.Recipient);
        Assert.AreEqual(Operation.CreatePageAction<Participant?, EmailPage>(nameof(EmailPage.ChangeEmailAddressAsync), ("oldEmailAddress", ParticipantEmailAddress)), email.Operation);
    }

    [TestMethod]
    public async Task ContinueRemovesAliases()
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            var other = new Participant("other@example.org")
            {
                GivenName = "aLiCe",
                FamilyName = "ApPlE",
                Status = ParticipantStatus.ProfileFilled
            };
            participant.PossibleAliasEmailAddresses = ["other@example.org"];
            Db.Participants.Add(other);
            await Db.CommitAsync();
        }

        {
            var result = await new AliasPage(DisabledEmailSender).ContinueAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsEmpty(newParticipant.PossibleAliasEmailAddresses);
    }
}