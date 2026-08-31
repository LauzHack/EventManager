using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.ChallengeSetterPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ChallengeSetterPages;

[TestClass]
public sealed class DescriptionPageTests : ChallengeSetterTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenDescriptionIsMissing()
    {
        await AddSetterAsync();

        var page = new DescriptionPage(EventStatus.CheckInStarted);
        var setter = await GetSetterAsync();
        var view = await page.ViewAsync(setter);

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsRequiredBeforeCheckInStarts()
    {
        await AddSetterAsync(description: "Cool challenge");

        var page = new DescriptionPage(EventStatus.ApplicationsClosed);
        var setter = await GetSetterAsync();
        var view = await page.ViewAsync(setter);

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWhenDescriptionIsSetAfterCheckInStarts()
    {
        await AddSetterAsync(description: "Cool challenge");

        var page = new DescriptionPage(EventStatus.CheckInStarted);
        var setter = await GetSetterAsync();
        var view = await page.ViewAsync(setter);

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task EditSetsDescription()
    {
        await AddSetterAsync();

        var page = new DescriptionPage(EventStatus.ApplicationsClosed);
        var setter = await GetSetterAsync();
        var result = await page.EditAsync(setter, "My cool challenge!");

        Assert.AreEqual(Status.Success, result.Status);
        var newSetter = await GetSetterAsync();
        Assert.AreEqual("My cool challenge!", newSetter.Description);

    }

    [TestMethod]
    public async Task EditFailsWhenDescriptionIsTooLong()
    {
        await AddSetterAsync();

        var page = new DescriptionPage(EventStatus.ApplicationsClosed);
        var setter = await GetSetterAsync();
        var result = await page.EditAsync(setter, new string('x', ChallengeSetter.MaxDescriptionLength + 1));

        Assert.AreEqual(Status.UserError, result.Status);
    }
}