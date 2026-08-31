using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class ProjectPage(DbValues<Project> projects, DbValues<ChallengeSetter> challengeSetters,
                                EventStatus eventStatus, EventLimits eventLimits,
                                FileStorage fileStorage, TimeProvider timeProvider) : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant)
    {
        if (eventLimits.ProjectTeamSize == 0 || participant.Status < ParticipantStatus.CheckedIn)
        {
            return ForbiddenView();
        }

        var project = await projects.FirstOrDefaultAsync(p => p.Team.Contains(participant));
        if (project is null)
        {
            return RequiredView("Project");
        }

        // It's important to show everything, in the last few minutes of an event,
        // participants are stressed and may freak out if they don't see some properties, thinking they weren't saved
        PageSummaryItem[] summary = [
            ("Title", project.Title),
            ("Short description", project.ShortDescription),
            ("Long description", project.LongDescription),
            ("Home page", project.Link),
            ("Challenges", project.Challenges.Length == 0 ? "None" : string.Join(", ", project.Challenges))
        ];
        if (eventStatus < EventStatus.JudgingStarted)
        {
            return EditableView("Project", "Edit", summary);
        }
        return SummaryOnlyView("Project", summary);
    }

    public override async Task<object?> GetModelAsync(Participant participant)
        => new Model(
               await projects.FirstOrDefaultAsync(p => p.Team.Contains(participant)),
               await projects.Where(p => p.InvitedParticipants.Contains(participant)).ToCollectionAsync(),
               await challengeSetters.OrderBy(c => c.Order).ToCollectionAsync()
           );

    public async Task<StatusMessage> JoinAsync(Participant participant, string id)
    {
        var project = await projects.FindAsync(id);
        if (project is null)
        {
            return Error("This project no longer exists");
        }
        if (!project.InvitedParticipants.Contains(participant))
        {
            return Error($"You have not been invited to **{project.Title}**.");
        }

        project.InvitedParticipants.Remove(participant);
        project.Team.Add(participant);

        // Handle the "oops, Alice forgot to invite Bob in the system, but they both went to demo their project, and now Bob wants travel reimbursement..." case
        if (project.Team.Any(p => p.Status == ParticipantStatus.Demoed))
        {
            participant.Status = ParticipantStatus.Demoed;
        }

        return Success($"You joined **{project.Title}**.");
    }

    public async Task<StatusMessage> EditAsync(Participant participant,
                                               string title,
                                               string shortDescription,
                                               string longDescription,
                                               string link,
                                               File? thumbnail,
                                               string[] challenges)
    {
        if (title.Length > Project.MaxTitleLength)
        {
            return Error($"Title is too long, max is {Project.MaxTitleLength} characters.");
        }
        if (shortDescription.Length > Project.MaxShortDescriptionLength)
        {
            return Error($"Short description is too long, max is {Project.MaxShortDescriptionLength} characters.");
        }
        if (longDescription.Length > Project.MaxLongDescriptionLength)
        {
            return Error($"Long description is too long, max is {Project.MaxLongDescriptionLength} characters.");
        }

        var project = await projects.FirstOrDefaultAsync(p => p.Team.Contains(participant));

        // In the unlikely event of a name collision, add a number at the end. Keeep going until it's unique.
        // This is inefficient but it's deterministic and should happen rarely, even never for most events.
        int expectedCollisions = title.Equals(project?.Title, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        string originalTitle = title;
        int titleExtraIndex = 2;
        while ((await projects.CountAsync(p => p.Title == title)) > expectedCollisions)
        {
            title = $"{originalTitle} ({titleExtraIndex.ToString(CultureInfo.InvariantCulture)})";
            titleExtraIndex += 1;
        }

        if (project is null)
        {
            if (thumbnail is null)
            {
                return Error("Please add a thumbnail to create your project.");
            }
            var id = DeterministicId.Create(participant.EmailAddress, timeProvider);
            var thumbnailId = await fileStorage.StoreFileAsync(thumbnail);
            project = new(id, title, shortDescription, longDescription, link, thumbnailId) { Team = { participant }, Challenges = challenges };
            projects.Add(project);
        }
        else
        {
            project.Title = title;
            project.ShortDescription = shortDescription;
            project.LongDescription = longDescription;
            project.Link = link;
            project.Challenges = challenges;
            if (thumbnail is not null)
            {
                await fileStorage.DeleteFileAsync(project.ThumbnailId);
                project.ThumbnailId = await fileStorage.StoreFileAsync(thumbnail);
            }
        }

        if (!title.Equals(originalTitle, StringComparison.Ordinal))
        {
            return Success("**A project with this name already exists**. A unique suffix was added to yours to avoid the collision.");
        }

        return Success("You updated the project successfully.");
    }

    public sealed record Model(Project? Project, IReadOnlyCollection<Project> InvitedProjects, IReadOnlyCollection<ChallengeSetter> ChallengeSetters);
}