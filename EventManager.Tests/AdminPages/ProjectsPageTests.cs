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
public sealed class ProjectsPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenWhenProjectsAreDisabled()
    {
        var page = new ProjectsPage(Db.Projects, EventStatus.CheckInStarted, EventLimits with { ProjectTeamSize = 0 });
        var view = await page.ViewAsync(await GetAdminAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWhenProjectsAreEnabled()
    {
        var page = new ProjectsPage(Db.Projects, EventStatus.CheckInStarted, EventLimits);
        var view = await page.ViewAsync(await GetAdminAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
        Assert.IsNotNull(view.Action);
    }

    [TestMethod]
    public async Task ModelIsProjectsSortedByTitle()
    {
        {
            var participant1 = new Participant("alice@example.org");
            var participant2 = new Participant("bob@example.org");
            var participant3 = new Participant("carol@example.org");
            var project1 = new Project("b", "BBB", "1", "1", "1", "1") { Team = { participant2 } };
            var project2 = new Project("a", "AAA", "2", "2", "2", "2") { Team = { participant1, participant3 } };
            Db.Participants.Add(participant1, participant2, participant3);
            Db.Projects.Add(project1, project2);
            await Db.CommitAsync();
        }

        var page = new ProjectsPage(Db.Projects, EventStatus.CheckInStarted, EventLimits);
        var modelAsObject = await page.GetModelAsync(await GetAdminAsync());

        var model = Assert.IsInstanceOfType<IReadOnlyCollection<Project>>(modelAsObject);
        Assert.AreSequenceEqual(["AAA", "BBB"], model.Select(p => p.Title));
    }

    [TestMethod]
    public async Task MarkAsDemoedDoesSo()
    {
        {
            var participant1 = new Participant("alice@example.org") { Status = ParticipantStatus.DeclaredTravelExpenses };
            var participant2 = new Participant("bob@example.org") { Status = ParticipantStatus.CheckedIn };
            var project = new Project("b", "BBB", "1", "1", "1", "1") { Team = { participant1, participant2 } };
            Db.Participants.Add(participant1, participant2);
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        {
            var page = new ProjectsPage(Db.Projects, EventStatus.JudgingStarted, EventLimits);
            var result = await page.MarkAsDemoedAsync("b");
            await Db.CommitAsync();
            Assert.AreEqual(Status.Success, result.Status);
        }

        var newParticipant1 = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(newParticipant1);
        Assert.AreEqual(ParticipantStatus.Demoed, newParticipant1.Status);
        var newParticipant2 = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newParticipant2);
        Assert.AreEqual(ParticipantStatus.Demoed, newParticipant2.Status);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.Demoed)]
    public async Task MarkAsDemoedFailsIfParticipantsAreNotCheckedInOrHaveDemoedAlready(ParticipantStatus status)
    {
        {
            var participant1 = new Participant("alice@example.org") { Status = status };
            var participant2 = new Participant("bob@example.org") { Status = status };
            var project = new Project("b", "BBB", "1", "1", "1", "1") { Team = { participant1, participant2 } };
            Db.Participants.Add(participant1, participant2);
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        var page = new ProjectsPage(Db.Projects, EventStatus.JudgingStarted, EventLimits);
        var result = await page.MarkAsDemoedAsync("b");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task MarkAsDemoedFailsForUnknownId()
    {
        var page = new ProjectsPage(Db.Projects, EventStatus.JudgingStarted, EventLimits);
        var result = await page.MarkAsDemoedAsync("doesnotexist");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task MarkAsDemoedFailsIfJudgingHasNotStarted()
    {
        {
            var participant1 = new Participant("alice@example.org") { Status = ParticipantStatus.DeclaredTravelExpenses };
            var participant2 = new Participant("bob@example.org") { Status = ParticipantStatus.CheckedIn };
            var project = new Project("b", "BBB", "1", "1", "1", "1") { Team = { participant1, participant2 } };
            Db.Participants.Add(participant1, participant2);
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        var page = new ProjectsPage(Db.Projects, EventStatus.CheckInClosed, EventLimits);
        var result = await page.MarkAsDemoedAsync("b");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task MarkAsNotDemoedDoesSo()
    {
        {
            var participant1 = new Participant("alice@example.org") { Status = ParticipantStatus.Demoed };
            var participant2 = new Participant("bob@example.org") { Status = ParticipantStatus.Demoed };
            var project = new Project("b", "BBB", "1", "1", "1", "1") { Team = { participant1, participant2 } };
            Db.Participants.Add(participant1, participant2);
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        {
            var page = new ProjectsPage(Db.Projects, EventStatus.JudgingStarted, EventLimits);
            var result = await page.MarkAsNotDemoedAsync("b");
            await Db.CommitAsync();
            Assert.AreEqual(Status.Success, result.Status);
        }

        var newParticipant1 = await Db.Participants.FindAsync("alice@example.org");
        Assert.IsNotNull(newParticipant1);
        Assert.AreEqual(ParticipantStatus.DeclaredTravelExpenses, newParticipant1.Status);
        var newParticipant2 = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newParticipant2);
        Assert.AreEqual(ParticipantStatus.DeclaredTravelExpenses, newParticipant2.Status);
    }

    [TestMethod]
    public async Task MarkAsNotDemoedFailsIfParticipantsAreNotMarkedAsDemoed()
    {
        {
            var participant1 = new Participant("alice@example.org") { Status = ParticipantStatus.DeclaredTravelExpenses };
            var participant2 = new Participant("bob@example.org") { Status = ParticipantStatus.CheckedIn };
            var project = new Project("b", "BBB", "1", "1", "1", "1") { Team = { participant1, participant2 } };
            Db.Participants.Add(participant1, participant2);
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        var page = new ProjectsPage(Db.Projects, EventStatus.JudgingStarted, EventLimits);
        var result = await page.MarkAsNotDemoedAsync("b");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task MarkAsNotDemoedFailsForUnknownId()
    {
        var page = new ProjectsPage(Db.Projects, EventStatus.JudgingStarted, EventLimits);
        var result = await page.MarkAsNotDemoedAsync("doesnotexist");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task MarkAsNotDemoedFailsIfJudgingHasNotStarted()
    {
        {
            var participant1 = new Participant("alice@example.org") { Status = ParticipantStatus.DeclaredTravelExpenses };
            var participant2 = new Participant("bob@example.org") { Status = ParticipantStatus.CheckedIn };
            var project = new Project("b", "BBB", "1", "1", "1", "1") { Team = { participant1, participant2 } };
            Db.Participants.Add(participant1, participant2);
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        var page = new ProjectsPage(Db.Projects, EventStatus.CheckInClosed, EventLimits);
        var result = await page.MarkAsNotDemoedAsync("b");
        Assert.AreEqual(Status.UserError, result.Status);
    }
}