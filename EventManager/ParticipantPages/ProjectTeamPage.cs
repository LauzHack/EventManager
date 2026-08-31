using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class ProjectTeamPage(DbValues<Participant> participants, DbValues<ApplicationGroup> groups, DbValues<Project> projects,
                                    EventLimits eventLimits,
                                    EmailSender emailSender) : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant)
    {
        var project = await projects.FirstOrDefaultAsync(p => p.Team.Contains(participant));
        if (project is null)
        {
            return ForbiddenView();
        }
        return RequiredView("Project submitted");
    }

    public override async Task<object?> GetModelAsync(Participant participant)
        => new Model(
               await groups.FirstAsync(g => g.Members.Contains(participant)),
               await projects.FirstAsync(p => p.Team.Contains(participant))
           );

    public async Task<StatusMessage> InviteAsync(Participant participant, string emailAddress)
    {
        var project = await projects.FirstAsync(p => p.Team.Contains(participant));
        if (project.Team.Count + project.InvitedParticipants.Count >= eventLimits.ProjectTeamSize)
        {
            return Error($"You cannot invite more members, the maximum project size is {eventLimits.ProjectTeamSize}.");
        }
        if (participant.EmailAddress.Equals(emailAddress, StringComparison.OrdinalIgnoreCase))
        {
            return Error("You cannot invite yourself...");
        }

        var newMember = await participants.FindAsync(emailAddress);
        if (newMember is null)
        {
            return Error($"There is no one with email address **{emailAddress}**, please check the spelling.");
        }
        if (newMember.Status < ParticipantStatus.CheckedIn)
        {
            return Error($"**{newMember.FullName}** has not checked into the event yet.");
        }

        project.InvitedParticipants.Add(newMember);
        await emailSender.SendEmailAsync(
            recipient: newMember.EmailAddress,
            subject: "Project invitation",
            body: $"{participant.FullName} has invited you to join the project '{project.Title}'.",
            operation: Operation.CreatePageView<Participant>()
        );

        return Success($"You invited **{newMember.FullName}** to your project.");
    }

    public async Task<StatusMessage> CancelInvitationAsync(Participant participant, string emailAddress)
    {
        var invitee = await participants.FindAsync(emailAddress);
        if (invitee is null)
        {
            return Error($"There is no one with email address **{emailAddress}**, perhaps they changed their email at the same time you sent this request?");
        }

        var project = await projects.FirstAsync(p => p.Team.Contains(participant));
        if (project.InvitedParticipants.Remove(invitee))
        {
            await emailSender.SendEmailAsync(
                recipient: emailAddress,
                subject: "Canceled project invitation",
                body: $"{participant.FullName} canceled their invitation to the project '{project.Title}'.",
                operation: null
            );
        }

        return Success($"You canceled the invitation for **{invitee.FullName}**.");
    }

    public async Task<StatusMessage> RemoveMemberAsync(Participant participant, string emailAddress)
    {
        var project = await projects.FirstOrDefaultAsync(p => p.Team.Contains(participant));
        if (project is null)
        {
            if (participant.EmailAddress.Equals(emailAddress, StringComparison.OrdinalIgnoreCase))
            {
                // for idempotency's sake, if someone sends the same "leave my project" request twice accidentally...
                return NoChange();
            }
            return Error("You cannot remove someone from your project as you are no longer in a project.");
        }

        var member = project.Team.FirstOrDefault(m => m.EmailAddress.Equals(emailAddress, StringComparison.OrdinalIgnoreCase));
        if (member is null)
        {
            // It's OK, asking to remove someone that's already removed isn't problematic.
            // Fetch the member from the DB so we can have a nice message with the name at the end.
            member = await participants.FindAsync(emailAddress);
            if (member is null)
            {
                return Error($"No participant has email **{emailAddress}**. Did they change it?");
            }
        }
        else
        {
            project.Team.Remove(member);
            if (project.Team.Count == 0)
            {
                projects.Remove(project);
            }
        }

        if (participant.EmailAddress.Equals(emailAddress, StringComparison.OrdinalIgnoreCase))
        {
            return Success($"You left **{project.Title}**.");
        }
        return Success($"You removed **{member.FullName}** from **{project.Title}**.");
    }

    public sealed record Model(ApplicationGroup Group, Project Project);
}