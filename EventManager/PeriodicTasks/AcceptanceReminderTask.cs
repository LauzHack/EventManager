using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

namespace EventManager.PeriodicTasks;

public sealed class AcceptanceReminderTask(DbValues<ApplicationGroup> groups, EventLimits? limits, EmailSender emailSender, TimeProvider timeProvider) : PeriodicTask
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
        var allGroups = await groups.ToCollectionAsync();
        var toRemind = allGroups.Where(g => now - g.AcceptanceDate >= TimeSpan.FromDays(limits.DaysBetweenReminders))
                                .SelectMany(g => g.Members.Where(p => p.Status == ParticipantStatus.Accepted))
                                .Where(p => p.LastStatusReminderDate.HasValue && now - p.LastStatusReminderDate.Value >= TimeSpan.FromDays(limits.DaysBetweenReminders))
                                .ToArray();

        await emailSender.SendAsync([.. toRemind.Select(participant => new Email(
            Recipient: participant.EmailAddress,
            Subject: "Reminder to confirm",
            Body: "You were accepted to the event but have not confirmed your participation yet.\n\n"
                + $"**Please confirm your participation using the link below as soon as possible.**",
            Operation: Operation.CreatePageAction<Participant, WaitForAcceptancePage>(nameof(WaitForAcceptancePage.ConfirmAsync)),
            OperationDescription: "Confirm"
        ))]);

        foreach (var participant in toRemind)
        {
            participant.LastStatusReminderDate = now;
        }

        if (toRemind.Length > 0)
        {
            return $"Reminded {toRemind.Length} participant(s) who have not yet confirmed but still can.";
        }
        return null;
    }
}