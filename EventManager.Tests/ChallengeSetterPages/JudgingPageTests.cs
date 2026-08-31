using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.ChallengeSetterPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ChallengeSetterPages;

[TestClass]
public sealed class JudgingPageTests : ChallengeSetterTestsBase
{
    [TestMethod]
    [DataRow(EventStatus.CheckInStarted)]
    [DataRow(EventStatus.CheckInClosed)]
    [DataRow(EventStatus.JudgingStarted)]
    [DataRow(EventStatus.Finished)]
    public async Task PageIsAlwaysRequired(EventStatus status)
    {
        await AddSetterAsync(description: "Cool challenge");

        var page = new JudgingPage(Db.Projects, status);
        var setter = await GetSetterAsync();
        var view = await page.ViewAsync(setter);

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task ModelHasAllProjectsForNonOptInChallenge()
    {
        await AddSetterAsync(isOptIn: false, description: "Cool challenge");
        await AddProjectsAsync([], [], []);

        var page = new JudgingPage(Db.Projects, EventStatus.JudgingStarted);
        var setter = await GetSetterAsync();
        var model = await page.GetModelAsync(setter);
        var typedModel = Assert.IsInstanceOfType<IReadOnlyCollection<Project>>(model);

        // and they're sorted case-insensitive!
        Assert.AreSequenceEqual(["Aaa", "xxx", "Zzz"], typedModel.Select(p => p.Title), StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task ModelOnlyHasOptedInProjectsForOptInChallenge()
    {
        await AddSetterAsync(isOptIn: true, description: "Cool challenge");
        await AddProjectsAsync([SetterName], ["other"], ["yet another", SetterName]);

        var page = new JudgingPage(Db.Projects, EventStatus.JudgingStarted);
        var setter = await GetSetterAsync();
        var model = await page.GetModelAsync(setter);
        var typedModel = Assert.IsInstanceOfType<IReadOnlyCollection<Project>>(model);

        // and they're sorted case-insensitive!
        Assert.AreSequenceEqual(["xxx", "Zzz"], typedModel.Select(p => p.Title), StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task EditSetsAwards()
    {
        await AddSetterAsync(isOptIn: false, description: "Cool challenge");
        await AddProjectsAsync([], [], []);

        var page = new JudgingPage(Db.Projects, EventStatus.JudgingStarted);
        var setter = await GetSetterAsync();
        var result = await page.EditAsync(setter, [new("1st place", "b"), new("2nd place", "a")]);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var newSetter = await GetSetterAsync();
        Assert.AreSequenceEqual([new(0, "1st place", "b"), new(1, "2nd place", "a")], newSetter.Awards);
    }

    [TestMethod]
    public async Task EditCanAddRemoveAndMoveAwards()
    {
        await AddSetterAsync(isOptIn: false, description: "Cool challenge");
        await AddProjectsAsync([], [], []);
        {
            var oldSetter = await GetSetterAsync();
            oldSetter.Awards.Add(new(0, "1st place", "b"));
            oldSetter.Awards.Add(new(1, "2nd place", "a"));
            await Db.CommitAsync();
        }

        var page = new JudgingPage(Db.Projects, EventStatus.JudgingStarted);
        var setter = await GetSetterAsync();
        var result = await page.EditAsync(setter, [new("2nd place", "a"), new("123rd place", "c")]);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var newSetter = await GetSetterAsync();
        Assert.AreSequenceEqual([new(0, "2nd place", "a"), new(1, "123rd place", "c")], newSetter.Awards);
    }

    [TestMethod]
    public async Task EditCanClearAwards()
    {
        await AddSetterAsync(isOptIn: false, description: "Cool challenge");
        await AddProjectsAsync([], [], []);
        {
            var oldSetter = await GetSetterAsync();
            oldSetter.Awards.Add(new(0, "1st place", "b"));
            oldSetter.Awards.Add(new(1, "2nd place", "a"));
            await Db.CommitAsync();
        }

        var page = new JudgingPage(Db.Projects, EventStatus.JudgingStarted);
        var setter = await GetSetterAsync();
        var result = await page.EditAsync(setter, []);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var newSetter = await GetSetterAsync();
        Assert.IsEmpty(newSetter.Awards);
    }

    private async Task AddProjectsAsync(string[] challenges0, string[] challenges1, string[] challenges2)
    {
        var alice = new Participant("alice@example.org") { Status = ParticipantStatus.CheckedIn };
        var bob = new Participant("bob@example.org") { Status = ParticipantStatus.CheckedIn };
        var carol = new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn };
        Db.Participants.Add(alice, bob, carol);
        Db.Projects.Add(
            new Project("a", "Zzz", "Last", "Last long", "https://example.org", "idA") { Team = { alice }, Challenges = challenges0 },
            new Project("b", "Aaa", "First", "First long", "https://example.org", "idB") { Team = { bob }, Challenges = challenges1 },
            new Project("c", "xxx", "Middle", "Middle long", "https://example.org", "idC") { Team = { carol }, Challenges = challenges2 }
        );
        await Db.CommitAsync();
    }
}