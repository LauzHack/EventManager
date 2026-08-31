using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class ChallengesPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsForbiddenWhenProjectsAreDisabled()
    {
        var admin = await GetAdminAsync();
        var page = GetPage(new(4, 0, 10, 3));
        var view = await page.ViewAsync(admin);

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWhenProjectsAreEnabled()
    {
        var admin = await GetAdminAsync();
        var page = GetPage();
        var view = await page.ViewAsync(admin);

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task ModelFiltersProjectsForOptInChallenges()
    {
        {
            var alice = new Participant("alice@example.org") { Status = ParticipantStatus.CheckedIn };
            var bob = new Participant("bob@example.org") { Status = ParticipantStatus.CheckedIn };
            var carol = new Participant("carol@example.org") { Status = ParticipantStatus.CheckedIn };
            Db.Participants.Add(alice, bob, carol);
            Db.Projects.Add(
                new Project("a", "Zzz", "Last", "Last long", "https://example.org", "idA") { Team = { alice }, Challenges = ["First", "Awarder"] },
                new Project("b", "Aaa", "First", "First long", "https://example.org", "idB") { Team = { bob }, Challenges = ["Awarder"] },
                new Project("c", "xxx", "Middle", "Middle long", "https://example.org", "idC") { Team = { carol }, Challenges = ["First"] }
            );
            Db.ChallengeSetters.Add(
                new("First", 0, true),
                new("Second", 1, false),
                new("Awarder", 2, true) { Awards = { new(0, "Top", "a") } }
            );
            await Db.CommitAsync();
        }

        var page = GetPage();
        var admin = await GetAdminAsync();
        var model = await page.GetModelAsync(admin);
        var typedModel = Assert.IsInstanceOfType<IReadOnlyCollection<ChallengesPage.ChallengeSetterAndProjects>>(model);

        // and they're sorted case-insensitive with awards first!
        Assert.HasCount(3, typedModel);
        Assert.AreEqual("First", typedModel.ElementAt(0).ChallengeSetter.Name, StringComparer.Ordinal);
        Assert.AreSequenceEqual(["xxx", "Zzz"], typedModel.ElementAt(0).Projects.Select(p => p.Key.Title), StringComparer.Ordinal);
        Assert.AreEqual("Second", typedModel.ElementAt(1).ChallengeSetter.Name, StringComparer.Ordinal);
        Assert.AreSequenceEqual(["Aaa", "xxx", "Zzz"], typedModel.ElementAt(1).Projects.Select(p => p.Key.Title), StringComparer.Ordinal);
        Assert.AreEqual("Awarder", typedModel.ElementAt(2).ChallengeSetter.Name, StringComparer.Ordinal);
        Assert.AreSequenceEqual(["Zzz", "Aaa"], typedModel.ElementAt(2).Projects.Select(p => p.Key.Title), StringComparer.Ordinal);
        Assert.AreSequenceEqual(["Awarder Top"], typedModel.ElementAt(2).Projects.ElementAt(0).Value, StringComparer.Ordinal);
        Assert.IsEmpty(typedModel.ElementAt(2).Projects.ElementAt(1).Value);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task EditCanCreateOrChangeNothing(bool existing)
    {
        if (existing)
        {
            Db.ChallengeSetters.Add(
                new("Company One", 0, false) { Description = "One Description" },
                new("Second Company", 1, true) { Description = "ok" }
            );
            await Db.CommitAsync();
        }

        var page = GetPage();
        var result = await page.EditAsync([new("Company One", false), new("Second Company", true)]);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var setters = await Db.ChallengeSetters.OrderBy(c => c.Order).ToCollectionAsync();
        Assert.HasCount(2, setters);
        Assert.AreEqual("Company One", setters.ElementAt(0).Name, StringComparer.Ordinal);
        Assert.IsFalse(setters.ElementAt(0).IsChallengeOptIn);
        Assert.AreEqual("Second Company", setters.ElementAt(1).Name, StringComparer.Ordinal);
        Assert.IsTrue(setters.ElementAt(1).IsChallengeOptIn);
        if (existing)
        {
            Assert.AreEqual("One Description", setters.ElementAt(0).Description, StringComparer.Ordinal);
            Assert.AreEqual("ok", setters.ElementAt(1).Description, StringComparer.Ordinal);
        }
        else
        {
            Assert.IsNull(setters.ElementAt(0).Description);
            Assert.IsNull(setters.ElementAt(1).Description);
        }
    }

    [TestMethod]
    public async Task EditCanAddEditMoveAndRemove()
    {
        {
            Db.ChallengeSetters.Add(
                new("Company One", 0, false) { Description = "One Description" },
                new("Second Company", 1, true) { Description = "ok" },
                new("Yet Another", 2, false) { Description = "bla bla" }
            );
            await Db.CommitAsync();
        }

        var page = GetPage();
        var result = await page.EditAsync([new("Second Company", false), new("Other one", true), new("Yet Another", true)]);
        Assert.AreEqual(Status.Success, result.Status);
        Assert.AreEqual("Edited challenge setters:\n- **Removed Company One**\n- Added Other one\n- Made Second Company opt-out\n- Made Yet Another opt-in\n- Moved Second Company", result.Text);
        await Db.CommitAsync();

        var setters = await Db.ChallengeSetters.OrderBy(c => c.Order).ToCollectionAsync();
        Assert.HasCount(3, setters);
        Assert.AreEqual("Second Company", setters.ElementAt(0).Name, StringComparer.Ordinal);
        Assert.IsFalse(setters.ElementAt(0).IsChallengeOptIn);
        Assert.AreEqual("ok", setters.ElementAt(0).Description, StringComparer.Ordinal);
        Assert.AreEqual("Other one", setters.ElementAt(1).Name, StringComparer.Ordinal);
        Assert.IsTrue(setters.ElementAt(1).IsChallengeOptIn);
        Assert.IsNull(setters.ElementAt(1).Description);
        Assert.AreEqual("Yet Another", setters.ElementAt(2).Name, StringComparer.Ordinal);
        Assert.IsTrue(setters.ElementAt(2).IsChallengeOptIn);
        Assert.AreEqual("bla bla", setters.ElementAt(2).Description, StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task EditFailsGivenIdenticalNames()
    {
        var page = GetPage();
        var result = await page.EditAsync([new("Name", false), new("Other", false), new("Name", true)]);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    private ChallengesPage GetPage(EventLimits? limits = null)
        => new(Db.ChallengeSetters, Db.Projects, limits ?? EventLimits);
}