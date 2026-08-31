using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class RejectedPageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenWithoutParticipant()
    {
        var view = await new RejectedPage().ViewAsync(null);

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.CheckedIn, false)]
    [DataRow(ParticipantStatus.Accepted, false)]
    [DataRow(ParticipantStatus.Created, false)]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation, false)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation, false)]
    [DataRow(ParticipantStatus.Rejected, true)]
    public async Task PageIsRequiredOnlyWhenParticipantIsRejected(ParticipantStatus participantStatus, bool isRequired)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = participantStatus;
            await Db.CommitAsync();
        }

        var view = await new RejectedPage().ViewAsync(await GetParticipantAsync());

        Assert.AreEqual(isRequired, view.IsRequired);
    }
}