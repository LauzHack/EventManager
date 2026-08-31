using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class ProjectTeamPageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenWhenParticipantHasNoProject()
    {
        var page = CreatePage(disableEmails: true);
        var result = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(result.IsRequired);
        Assert.IsFalse(result.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsRequiredWhenParticipantHasAProject()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Participants.Add(
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.CheckedIn
                }
            );
            Db.Projects.Add(new("Id", "Title", "Description", "Long", "https://example.org", "abc") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(result.IsRequired);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)] // test idempotency
    public async Task InviteSucceedsWhenParticipantIsAlone(int count)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Participants.Add(
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.CheckedIn
                }
            );
            Db.Projects.Add(new("Id", "Title", "Description", "Long", "https://example.org", "abc") { Team = { participant } });
            await Db.CommitAsync();
        }

        for (int n = 0; n < count; n++)
        {
            var page = CreatePage();
            var result = await page.InviteAsync(await GetParticipantAsync(), "bob@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNotNull(newProject);
        Assert.AreSequenceEqual(["bob@example.org"], [.. newProject.InvitedParticipants.Select(p => p.EmailAddress)]);
    }

    [TestMethod]
    public async Task InviteFailsWhenProjectTeamHasReachedSizeLimit()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var project = new Project("Id", "Title", "Description", "Long", "https://example.org", "abc") { Team = { participant } };
            for (int n = 0; n < EventLimits.ProjectTeamSize - 1; n++)
            {
                var p = new Participant(n.ToString(CultureInfo.InvariantCulture) + "@example.org")
                {
                    Status = ParticipantStatus.CheckedIn,
                };
                project.Team.Add(p);
                Db.Participants.Add(p);
            }
            Db.Participants.Add(
                new Participant("last@example.org")
                {
                    Status = ParticipantStatus.CheckedIn
                }
            );
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        var page = CreatePage();
        var result = await page.InviteAsync(await GetParticipantAsync(), "last@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task InviteFailsWhenProjectHasReachedSizeLimitDueToInvitations()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var project = new Project("Id", "Title", "Description", "Long", "https://example.org", "abc") { Team = { participant } };
            var invited = new Participant("someone@example.org")
            {
                Status = ParticipantStatus.CheckedIn
            };
            Db.Participants.Add(invited);
            project.InvitedParticipants.Add(invited);
            for (int n = 0; n < EventLimits.ProjectTeamSize - 2; n++)
            {
                var p = new Participant(n.ToString(CultureInfo.InvariantCulture) + "@example.org")
                {
                    Status = ParticipantStatus.CheckedIn,
                };
                project.Team.Add(p);
                Db.Participants.Add(p);
            }
            Db.Participants.Add(
                new Participant("last@example.org")
                {
                    Status = ParticipantStatus.CheckedIn
                }
            );
            Db.Projects.Add(project);
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.InviteAsync(await GetParticipantAsync(), "last@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task InviteFailsForUnknownEmailAddress()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.InviteAsync(await GetParticipantAsync(), "missing@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task InviteFailsForNonCheckedInPerson()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Participants.Add(
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.Confirmed
                }
            );
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.InviteAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task InviteFailsForOneself(bool toUpper)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.InviteAsync(await GetParticipantAsync(), toUpper ? ParticipantEmailAddress.ToUpperInvariant() : ParticipantEmailAddress);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelInvitationFailsWhenParticipantHasNoProject()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.CancelInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task CancelInvitationSucceedsWhenPersonWasInvited()
    {
        {
            var participant = await GetParticipantAsync();
            var other = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.CheckedIn
            };
            Db.Participants.Add(other);
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "Description", "Long", "https://example.org", "abc")
            {
                Team = { participant },
                InvitedParticipants = { other }
            });
            await Db.CommitAsync();
        }

        var page = CreatePage();
        var result = await page.CancelInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.Success, result.Status);
        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNotNull(newProject);
        Assert.IsEmpty(newProject.InvitedParticipants);
    }

    [TestMethod]
    public async Task CancelInvitationSucceedsWhenPersonWasNotInvited()
    {
        {
            var participant = await GetParticipantAsync();
            Db.Participants.Add(
                new Participant("bob@example.org")
                {
                    Status = ParticipantStatus.CheckedIn
                }
            );
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.CancelInvitationAsync(await GetParticipantAsync(), "bob@example.org");

        Assert.AreEqual(Status.Success, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task CancelInvitationFailsForUnknownEmailAddress()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.CancelInvitationAsync(await GetParticipantAsync(), "missing@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RemoveYourselfSucceedsWhenParticipantWasAlone(bool sameCase)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        {
            var page = CreatePage(disableEmails: true);
            var result = await page.RemoveMemberAsync(await GetParticipantAsync(), sameCase ? ParticipantEmailAddress : ParticipantEmailAddress.ToUpperInvariant());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }


        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNull(newProject);

        Assert.AreEqual(0, await Db.Projects.CountAsync());
    }

    [TestMethod]
    public async Task RemoveYourselfSucceedsWhenParticipantWasNotAlone()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var other = new Participant("bob@example.org")
            {
                Status = ParticipantStatus.CheckedIn,
            };
            Db.Participants.Add(other);
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant, other } });
            await Db.CommitAsync();
        }

        {
            var page = CreatePage(disableEmails: true);
            var result = await page.RemoveMemberAsync(await GetParticipantAsync(), ParticipantEmailAddress);
            Assert.AreEqual(Status.Success, result.Status);
            Assert.Contains("you left", result.Text, StringComparison.OrdinalIgnoreCase);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNull(newProject);

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newOtherProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newOther));
        Assert.IsNotNull(newOtherProject);
        Assert.AreSequenceEqual([newOther], newOtherProject.Team);
    }

    [TestMethod]
    public async Task RemoveYourselfSucceedsWhenParticipantHadNoProject()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.RemoveMemberAsync(await GetParticipantAsync(), ParticipantEmailAddress);

        Assert.AreEqual(Status.None, result.Status);
        Assert.IsFalse(await Db.CommitAsync());
    }

    [TestMethod]
    public async Task RemoveMemberSucceedsWhenMemberIsInProject()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var other = new Participant("bob@example.org")
            {
                GivenName = "Bob",
                FamilyName = "Banana",
                Status = ParticipantStatus.CheckedIn,
            };
            Db.Participants.Add(other);
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant, other } });
            await Db.CommitAsync();
        }

        {
            var page = CreatePage(disableEmails: true);
            var result = await page.RemoveMemberAsync(await GetParticipantAsync(), "bob@example.org");
            Assert.AreEqual(Status.Success, result.Status);
            Assert.Contains("you removed **Bob Banana**", result.Text, StringComparison.OrdinalIgnoreCase);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        var newProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newParticipant));
        Assert.IsNotNull(newProject);
        Assert.AreSequenceEqual([newParticipant], newProject.Team);

        var newOther = await Db.Participants.FindAsync("bob@example.org");
        Assert.IsNotNull(newOther);
        var newOtherProject = await Db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(newOther));
        Assert.IsNull(newOtherProject);
    }

    [TestMethod]
    public async Task RemoveMemberSucceedsWhenMemberIsNotInProject()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var other = new Participant("bob@example.org")
            {
                GivenName = "Bob",
                FamilyName = "Banana",
                Status = ParticipantStatus.CheckedIn
            };
            Db.Participants.Add(other);
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.RemoveMemberAsync(await GetParticipantAsync(), "bob@example.org");
        Assert.AreEqual(Status.Success, result.Status);
        Assert.Contains("you removed **Bob Banana**", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task RemoveMemberFailsWhenParticipantIsNotInProject()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            var other = new Participant("bob@example.org")
            {
                GivenName = "Bob",
                FamilyName = "Banana",
                Status = ParticipantStatus.CheckedIn
            };
            Db.Participants.Add(other);
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.RemoveMemberAsync(await GetParticipantAsync(), "bob@example.org");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RemoveMemberFailsWhenMemberIsUnknown()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            Db.Projects.Add(new("Id", "Title", "ShortDescr", "LongDescription", "http://example.org", "123") { Team = { participant } });
            await Db.CommitAsync();
        }

        var page = CreatePage(disableEmails: true);
        var result = await page.RemoveMemberAsync(await GetParticipantAsync(), "unknown@example.org");
        Assert.AreEqual(Status.UserError, result.Status);
    }

    private ProjectTeamPage CreatePage(EventLimits? limits = null, bool disableEmails = false)
        => new(Db.Participants, Db.ApplicationGroups, Db.Projects, limits ?? EventLimits, disableEmails ? DisabledEmailSender : EmailSender);
}