using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.PeriodicTasks;

public sealed class FinalizationReminderTask(DbValues<Participant> participants, EventLimits? limits, EmailSender emailSender, TimeProvider timeProvider) : PeriodicTask
{
    public override async Task<string?> RunAsync()
    {
        if (limits is null)
        {
            // Not yet configured.
            return null;
        }

        var now = timeProvider.GetUtcNow();
        // EF Core on SQLite doesn't support TimeSpan yet: https://github.com/dotnet/efcore/issues/18844
        var toRemind = await participants
            .Where(p => p.Status == ParticipantStatus.ProfileFilled)
            .ToCollectionAsync();
        toRemind = [.. toRemind.Where(p => !p.LastStatusReminderDate.HasValue || now - p.LastStatusReminderDate.Value >= TimeSpan.FromDays(limits.DaysBetweenReminders))];

        await emailSender.SendAsync([.. toRemind.Select(participant => new Email(
            Recipient: participant.EmailAddress,
            Subject: "Reminder to finalize",
            Body: "You filled in your details but did not finalize your application yet. As it stands, your application cannot be considered.\n\n"
                + $"**Please finalize your application, alone or with a group, so that the organizers can process it.**\n\n"
                + "_If you no longer want to apply, you can also withdraw from the website._",
            Operation: Operation.CreatePageView<Participant>(),
            OperationDescription: "Go to the website"
        ))]);

        foreach (var participant in toRemind)
        {
            participant.LastStatusReminderDate = now;
        }

        if (toRemind.Count > 0)
        {
            return $"Reminded {toRemind.Count} participant(s) who filled their profile but have not finalized their application yet.";
        }
        return null;
    }
}