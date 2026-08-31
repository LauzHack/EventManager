using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class ProjectPageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenWhenProjectsAreDisabled()
    {
        await SetParticipantStatusAsync(ParticipantStatus.CheckedIn);

        var page = GetPage(EventStatus.CheckInStarted, EventLimits with { ProjectTeamSize = 0 });
        var view = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsHiddenWhenParticipantIsNotCheckedIn()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var page = GetPage(EventStatus.CheckInStarted);
        var view = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsRequiredWhenParticipantHasNoProject()
    {
        await SetParticipantStatusAsync(ParticipantStatus.CheckedIn);

        var page = GetPage(EventStatus.JudgingStarted);
        var view = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsEditableWhenParticipantHasAProjectAndProjectSubmissionsAreOpen()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = GetPage(EventStatus.CheckInClosed);
        var view = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);

        // Without seeing all details, participants freak out in the last minutes before submission
        // -4 because we don't show the ID, invitations, thumbnail, nor the team
        Assert.HasCount(typeof(Project).GetProperties().Length - 4, view.Summary);
    }

    [TestMethod]
    public async Task PageIsSummaryOnlyWhenProjectSubmissionsAreClosed()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = GetPage(EventStatus.JudgingStarted);
        var view = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);

        // Without seeing all details, participants freak out in the last minutes before submission
        // -4 because we don't show the ID, invitations, thumbnail, nor the team
        Assert.HasCount(typeof(Project).GetProperties().Length - 4, view.Summary);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ModelHasProject(bool exists)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            if (exists)
            {
                Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            }
            await Db.CommitAsync();
        }

        var page = GetPage(EventStatus.CheckInStarted);
        var modelAsObject = await page.GetModelAsync(await GetParticipantAsync());

        var model = Assert.IsInstanceOfType<ProjectPage.Model>(modelAsObject);
        if (exists)
        {
            Assert.IsNotNull(model.Project);
            Assert.AreEqual("Title", model.Project.Title);
        }
        else
        {
            Assert.IsNull(model.Project);
        }
    }

    [TestMethod]
    public async Task ModelHasInvitedProjects()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.CheckedIn,
            };
            var carol = new Participant("carol@example.org")
            {
                Status = ParticipantStatus.CheckedIn,
            };
            var someoneElse = new Participant("someone-else@example.org");
            Db.Participants.Add(bob, carol, someoneElse);
            Db.Projects.Add(
                new("Id", "Example", "Description", "Long", "https://example.org/1", "abc")
                {
                    Team = { bob },
                    InvitedParticipants = { participant }
                },
                new("Id2", "Example2", "Description2", "Long2", "https://example.org/2", "xyz")
                {
                    Team = { carol },
                    InvitedParticipants = { someoneElse }
                }
            );
            await Db.CommitAsync();
        }

        var page = GetPage(EventStatus.CheckInStarted);
        var modelAsObject = await page.GetModelAsync(await GetParticipantAsync());

        var model = Assert.IsInstanceOfType<ProjectPage.Model>(modelAsObject);
        Assert.AreSequenceEqual(["Example"], model.InvitedProjects.Select(p => p.Title));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "Had no project")]
    [DataRow(true, DisplayName = "Had existing project")]
    public async Task JoinSucceedsWhenInvited(bool hadProject)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            if (hadProject)
            {
                Db.Projects.Add(new("Id", "Xyzzy", "X", "LongX", "https://example.org/other", "xxx") { Team = { participant } });
            }
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.CheckedIn,
            };
            Db.Participants.Add(bob);
            Db.Projects.Add(new("Id2", "Example", "Description", "Long", "https://example.org/1", "abc")
            {
                Team = { bob },
                InvitedParticipants = { participant }
            });
            await Db.CommitAsync();
        }

        {
            var page = GetPage(EventStatus.CheckInStarted);
            var result = await page.JoinAsync(await GetParticipantAsync(), "Id2");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNotNull(newProject);
        Assert.AreEqual("Example", newProject.Title);
        Assert.AreSequenceEqual([ParticipantEmailAddress, "bob@example.org"], newProject.Team.Select(m => m.EmailAddress), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task JoinSetsStatusToDemoedIfInviterHasDemoed()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.Demoed,
            };
            Db.Participants.Add(bob);
            Db.Projects.Add(new("Id", "Example", "Description", "Long", "https://example.org/1", "abc")
            {
                Team = { bob },
                InvitedParticipants = { participant }
            });
            await Db.CommitAsync();
        }

        {
            var page = GetPage(EventStatus.JudgingStarted);
            var result = await page.JoinAsync(await GetParticipantAsync(), "Id");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual(ParticipantStatus.Demoed, newParticipant.Status);
    }

    [TestMethod]
    public async Task JoinFailsForUnknownId()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var page = GetPage(EventStatus.CheckInStarted);
        var result = await page.JoinAsync(await GetParticipantAsync(), "doesnotexist");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task JoinFailsWhenParticipantIsNotInvited()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.CheckedIn,
            };
            Db.Participants.Add(bob);
            Db.Projects.Add(new("Id", "Example", "Description", "Long", "https://example.org/1", "abc")
            {
                Team = { bob }
            });
            await Db.CommitAsync();
        }

        var page = GetPage(EventStatus.CheckInStarted);
        var result = await page.JoinAsync(await GetParticipantAsync(), "Id");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(EventStatus.CheckInStarted)]
    [DataRow(EventStatus.CheckInClosed)]
    [DataRow(EventStatus.JudgingStarted)]
    public async Task EditCreatesProjectIfNeeded(EventStatus eventStatus)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var thumbnail = new File.InMemory("name", "image/png", [0, 42]);
        string[] challenges = ["X", "YY"];
        string title = MakeString(Project.MaxTitleLength);
        string shortDescription = MakeString(Project.MaxShortDescriptionLength);
        string longDescription = MakeString(Project.MaxLongDescriptionLength);

        {
            var page = GetPage(eventStatus);
            var result = await page.EditAsync(await GetParticipantAsync(),
                title,
                shortDescription,
                longDescription,
                "example.org",
                thumbnail,
                challenges
            );
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNotNull(newProject);
        Assert.AreEqual(title, newProject.Title);
        Assert.AreEqual(shortDescription, newProject.ShortDescription);
        Assert.AreEqual(longDescription, newProject.LongDescription);
        Assert.AreEqual("example.org", newProject.Link);
        var storedFile = await FileStorage.GetFileAsync(newProject.ThumbnailId);
        Assert.IsNotNull(storedFile);
        Assert.AreEqual(thumbnail.MimeType, storedFile.MimeType);
        Assert.AreSequenceEqual(challenges, newProject.Challenges);
    }

    [TestMethod]
    public async Task EditFailsIfTitleIsTooLong()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var title = MakeString(Project.MaxTitleLength + 1);
        var thumbnail = new File.InMemory("name", "image/png", [0, 42]);
        string[] challenges = ["X", "YY"];

        var page = GetPage(EventStatus.CheckInStarted);
        var result = await page.EditAsync(await GetParticipantAsync(), title, "Description", "Long", "example.org", thumbnail, challenges);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsIfThumbnailIsMissingAndProjectDoesNotAlreadyExist()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        string[] challenges = ["X", "YY"];
        var page = GetPage(EventStatus.CheckInStarted);
        var result = await page.EditAsync(await GetParticipantAsync(), "Title", "Description", "Long", "example.org", null, challenges);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsIfShortDescriptionIsTooLong()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var shortDescription = MakeString(Project.MaxShortDescriptionLength + 1);
        var thumbnail = new File.InMemory("name", "image/png", [0, 42]);
        string[] challenges = ["X", "YY"];
        var page = GetPage(EventStatus.CheckInStarted);
        var result = await page.EditAsync(await GetParticipantAsync(), "Title", shortDescription, "long", "example.org", thumbnail, challenges);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsIfLongDescriptionIsTooLong()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var longDescription = MakeString(Project.MaxLongDescriptionLength + 1);
        var thumbnail = new File.InMemory("name", "image/png", [0, 42]);
        string[] challenges = ["X", "YY"];
        var page = GetPage(EventStatus.CheckInStarted);
        var result = await page.EditAsync(await GetParticipantAsync(), "Title", "short", longDescription, "example.org", thumbnail, challenges);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditSetsPropertiesWhenProjectAlreadyExists()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "Description", "Long", "https://example.org", "abc")
            {
                Team = { participant },
                Challenges = ["Q", "WE", "RTY"]
            });
            await Db.CommitAsync();
        }


        string[] challenges = ["X", "YY"];
        {
            var page = GetPage(EventStatus.CheckInStarted);
            var result = await page.EditAsync(await GetParticipantAsync(), "1", "2", "3", "4", null, challenges);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNotNull(newProject);
        Assert.IsNotNull(newProject);
        Assert.AreEqual("1", newProject.Title);
        Assert.AreEqual("2", newProject.ShortDescription);
        Assert.AreEqual("3", newProject.LongDescription);
        Assert.AreEqual("4", newProject.Link);
        Assert.AreEqual("abc", newProject.ThumbnailId);
        Assert.AreSequenceEqual(challenges, newProject.Challenges);
    }

    [TestMethod]
    public async Task EditReplacesThumbnailWhenProjectAlreadyExists()
    {
        var project = new Project("Id", "Title", "Description", "Long", "https://example.org", "abc")
        {
            Challenges = ["Q", "WE", "RTY"]
        };
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            project.Team.Add(participant);
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        var newThumbnail = new File.InMemory("name", "some/fake/mimetype", [6, 7, 8, 9]);
        {
            var page = GetPage(EventStatus.CheckInStarted);
            var result = await page.EditAsync(await GetParticipantAsync(), project.Title, project.ShortDescription, project.LongDescription, project.Link, newThumbnail, project.Challenges);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNotNull(newProject);

        var storedThumbnail = await FileStorage.GetFileAsync(newProject.ThumbnailId);
        Assert.IsNotNull(storedThumbnail);
        Assert.AreEqual(newThumbnail.MimeType, storedThumbnail.MimeType);

        var oldThumbnail = await FileStorage.GetFileAsync(project.ThumbnailId);
        Assert.IsNull(oldThumbnail);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "Same case")]
    [DataRow(true, DisplayName = "Different case")]
    public async Task EditCreatesProjectButAddsSuffixIfTitleAlreadyExists(bool toUpper)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var bob = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.CheckedIn,
            };
            Db.Participants.Add(bob);
            Db.Projects.Add(new("Id", "Title", "Description", "Long", "https://example.org", "abc")
            {
                Team = { bob }
            });
            await Db.CommitAsync();
        }

        string title = toUpper ? "TITLE" : "Title";
        var thumbnail = new File.InMemory("name", "image/png", [0, 42]);
        string[] challenges = ["X", "YY"];
        {
            var page = GetPage(EventStatus.CheckInStarted);
            var result = await page.EditAsync(await GetParticipantAsync(), title, "Other", "Long", "https://example.org/2", thumbnail, challenges);
            Assert.AreEqual(Status.Success, result.Status);
            Assert.Contains("suffix", result.Text, StringComparison.Ordinal);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNotNull(newProject);
        Assert.AreNotEqual(title, newProject.Title);
        Assert.StartsWith(title, newProject.Title, StringComparison.Ordinal);
        Assert.AreEqual("Other", newProject.ShortDescription);
        Assert.AreEqual("Long", newProject.LongDescription);
        Assert.AreEqual("https://example.org/2", newProject.Link);
        var storedFile = await FileStorage.GetFileAsync(newProject.ThumbnailId);
        Assert.IsNotNull(storedFile);
        Assert.AreEqual(thumbnail.MimeType, storedFile.MimeType);
        Assert.AreSequenceEqual(challenges, newProject.Challenges);
    }

    private ProjectPage GetPage(EventStatus status, EventLimits? limits = null)
        => new(Db.Projects, Db.ChallengeSetters, status, limits ?? EventLimits, FileStorage, TimeProvider);

    private string MakeString(uint length)
    {
        const int lineLength = 10;
        uint lineCount = length / lineLength;
        uint remaining = length % lineLength;
        return string.Join('\n', Enumerable.Repeat(new string('x', lineLength - 1), (int)lineCount))
            + "\n"
            + new string('x', (int)remaining);
    }
}