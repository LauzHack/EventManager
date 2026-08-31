using System.Threading.Tasks;

using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class WelcomePageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsRequired()
    {
        var view = await new WelcomePage().ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }
}