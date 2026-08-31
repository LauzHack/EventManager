using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class WithdrawnPageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenWithoutParticipant()
    {
        var view = await new WithdrawnPage().ViewAsync(null);

        Assert.IsFalse(view.IsRequired);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Created, false)]
    [DataRow(ParticipantStatus.Rejected, false)]
    [DataRow(ParticipantStatus.Accepted, false)]
    [DataRow(ParticipantStatus.CheckedIn, false)]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation, true)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation, true)]
    public async Task PageIsRequiredOnlyWhenParticipantIsWithdrawn(ParticipantStatus participantStatus, bool isRequired)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = participantStatus;
            await Db.CommitAsync();
        }

        var view = await new WithdrawnPage().ViewAsync(await GetParticipantAsync());

        Assert.AreEqual(isRequired, view.IsRequired);
        if (isRequired)
        {
            Assert.IsTrue(view.IsInteractable);
        }
    }

    [TestMethod]
    public async Task UndoMakesWithdrawnBeforeConfirmationParticipantEmailVerifiedAgain()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.WithdrawnBeforeConfirmation;
            await Db.CommitAsync();
        }

        {
            var result = await new WithdrawnPage().UndoAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.ImportantInformation, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual(ParticipantStatus.EmailAddressVerified, newParticipant.Status);
    }

    [TestMethod]
    public async Task UndoMakesWithdrawnAfterConfirmationParticipantAliasCheckedAgain()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.WithdrawnAfterConfirmation;
            await Db.CommitAsync();
        }

        {
            var result = await new WithdrawnPage().UndoAsync(await GetParticipantAsync());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual(ParticipantStatus.Confirmed, newParticipant.Status);
    }

    [TestMethod]
    public async Task UndoDoesNothingForCreatedParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Created;
            await Db.CommitAsync();
        }

        var result = await new WithdrawnPage().UndoAsync(await GetParticipantAsync());

        Assert.AreEqual(Status.None, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }
}