using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.PeriodicTasks;

public sealed class ConfirmationDelayEnforcementTask(DbValues<ApplicationGroup> groups,
                                                     EventLimits? limits, EventDetails? details,
                                                     EmailSender emailSender, TimeProvider timeProvider) : PeriodicTask
{
    public override async Task<string?> RunAsync()
    {
        if (limits is null || details is null)
        {
            // Not yet configured.
            return null;
        }

        var now = timeProvider.GetUtcNow();
        // EF Core on SQLite doesn't support TimeSpan yet: https://github.com/dotnet/efcore/issues/18844
        var allGroups = await groups.ToCollectionAsync();
        var didNotConfirm = allGroups.Where(g => now - g.AcceptanceDate >= TimeSpan.FromDays(limits.DaysToConfirm))
                                     .SelectMany(g => g.Members.Where(p => p.Status == ParticipantStatus.Accepted))
                                     .ToArray();

        await emailSender.SendAsync([.. didNotConfirm.Select(participant => new Email(
            Recipient: participant.EmailAddress,
            Subject: "You did not confirm in time",
            Body: $"Since you did not confirm your participation in time, **you will not be able to participate in {details}**.",
            Operation: null
        ))]);

        foreach (var participant in didNotConfirm)
        {
            participant.Status = ParticipantStatus.DidNotConfirm;
        }

        if (didNotConfirm.Length > 0)
        {
            return $"Dropped {didNotConfirm.Length} participant(s) who did not confirm in time.";
        }
        return null;
    }
}