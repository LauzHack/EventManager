using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class WaitForCheckInPageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenParticipantIsNotCheckedIn()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            await Db.CommitAsync();
        }

        var page = new WaitForCheckInPage();
        var view = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.CheckedIn)]
    [DataRow(ParticipantStatus.DeclaredTravelExpenses)]
    [DataRow(ParticipantStatus.Demoed)]
    public async Task PageIsForbiddenWhenParticipantCheckedIn(ParticipantStatus participantStatus)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = participantStatus;
            await Db.CommitAsync();
        }

        var page = new WaitForCheckInPage();
        var view = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }
}