using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class NamePageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWithoutGivenName()
    {
        var view = await new NamePage(Db.Participants).ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWithoutFamilyName()
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            await Db.CommitAsync();
        }

        var view = await new NamePage(Db.Participants).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWithFullName()
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            participant.Status = ParticipantStatus.Created;
            await Db.CommitAsync();
        }

        var view = await new NamePage(Db.Participants).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task PageIsSummaryOnlyWhenAtLeastFinalized(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            participant.Status = status;
            await Db.CommitAsync();
        }

        var view = await new NamePage(Db.Participants).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsNotEmpty(view.Summary);
    }

    [TestMethod]
    [DataRow("Apple")]
    [DataRow(null)]
    public async Task SummaryIsNameWhenParticipantWhoAlreadyFilledIt(string? familyName)
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = familyName;
            participant.Status = ParticipantStatus.ProfileFilled;
            await Db.CommitAsync();
        }

        var view = await new NamePage(Db.Participants).ViewAsync(await GetParticipantAsync());

        if (familyName == null)
        {
            Assert.AreSequenceEqual([("Given name", "Alice")], view.Summary);
        }
        else
        {
            Assert.AreSequenceEqual([("Given name", "Alice"), ("Family name", familyName)], view.Summary);
        }
    }

    [TestMethod]
    public async Task EditReturnsErrorIfGivenNameIsBlank()
    {
        var result = await new NamePage(Db.Participants).EditAsync(await GetParticipantAsync(), "  ", "Apple");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditReturnsErrorIfFamilyNameIsBlank()
    {
        var result = await new NamePage(Db.Participants).EditAsync(await GetParticipantAsync(), "Alice", "  ");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditSuccessfullyEditsWithFullName()
    {
        {
            var result = await new NamePage(Db.Participants).EditAsync(await GetParticipantAsync(), "Alice", "Apple");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.AreEqual("Alice", newParticipant.GivenName);
        Assert.AreEqual("Apple", newParticipant.FamilyName);
    }

    [TestMethod]
    public async Task EditSuccessfullyEditsWithEmptyFamilyNamePlaceholder()
    {
        {
            var result = await new NamePage(Db.Participants).EditAsync(await GetParticipantAsync(), "Alice", NamePage.EmptyFamilyNamePlaceholder);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.AreEqual("Alice", newParticipant.GivenName);
        Assert.IsNull(newParticipant.FamilyName);
    }

    [TestMethod]
    public async Task EditSuccessfullyOverwritesPreviousName()
    {
        {
            var participant = await GetParticipantAsync();
            participant.GivenName = "Alice";
            participant.FamilyName = "Apple";
            await Db.CommitAsync();
        }

        {
            var result = await new NamePage(Db.Participants).EditAsync(await GetParticipantAsync(), "Bob", NamePage.EmptyFamilyNamePlaceholder);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.AreEqual("Bob", newParticipant.GivenName);
        Assert.IsNull(newParticipant.FamilyName);
    }

    [TestMethod]
    public async Task EditDoesNotAddPossibleAliasesWhenThereAreNoMatches()
    {
        {
            Db.Participants.Add(
                new Participant("alice2@example.org") { GivenName = "Alice", FamilyName = null },
                new Participant("alice3@example.org") { GivenName = "Alice", FamilyName = "Banana" },
                new Participant("bob@example.org") { GivenName = "Bob", FamilyName = "Apple" },
                new Participant("bob2@example.org") { GivenName = "Bob", FamilyName = "Banana" }
            );
            await Db.CommitAsync();
        }

        {
            var page = new NamePage(Db.Participants);
            var result = await page.EditAsync(await GetParticipantAsync(), "Alice", "Apple");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await GetParticipantAsync();
        Assert.IsEmpty(participant.PossibleAliasEmailAddresses);
    }


    [TestMethod]
    [DataRow("Alice", "Apple")]
    [DataRow("alice", "aPPlE")]
    [DataRow("Apple", "alice")]
    public async Task EditIdentifiesPossibleAliases(string aliasGivenName, string aliasFamilyName)
    {
        {
            Db.Participants.Add(
                new Participant("alice2@example.org")
                {
                    GivenName = aliasGivenName,
                    FamilyName = aliasFamilyName
                }
            );
            await Db.CommitAsync();
        }
        {
            var page = new NamePage(Db.Participants);
            var result = await page.EditAsync(await GetParticipantAsync(), "Alice", "Apple");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var participant = await GetParticipantAsync();
        Assert.AreSequenceEqual(["alice2@example.org"], participant.PossibleAliasEmailAddresses);
    }
}