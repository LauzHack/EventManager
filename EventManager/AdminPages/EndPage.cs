using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class EndPage(IQueryable<Participant> participants, IQueryable<ChallengeSetter> challengeSetters,
                            ConfigValue<EventStatus> eventStatus,
                            EmailSender emailSender) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
        => RequiredView("Event management");

    public override async Task<object?> GetModelAsync(Admin user)
        => await challengeSetters.OrderBy(c => c.Order).ToCollectionAsync();

    public async Task<StatusMessage> SendLoginEmailToCheckedInAsync()
    {
        var checkedInParticipants = await participants.Where(p => p.Status >= ParticipantStatus.CheckedIn).ToCollectionAsync();
        var emails = new List<Email>();
        foreach (var checkedIn in checkedInParticipants)
        {
            emails.Add(new(
                Recipient: checkedIn.EmailAddress,
                Subject: "Log in",
                Body: "If you need it, here is a login link.",
                Operation: Operation.CreatePageView<Participant>(),
                OperationDescription: "Log in"
            ));
        }
        await emailSender.SendAsync(emails);
        return Success("All checked-in participants have been sent a login email.");
    }

    public Task<StatusMessage> StartJudgingAsync(Admin admin)
        => SetPhaseAsync(admin, EventStatus.CheckInClosed, "before judging starts", EventStatus.JudgingStarted, "Judging has started.");

    public Task<StatusMessage> CancelJudgingStartAsync(Admin admin)
        => SetPhaseAsync(admin, EventStatus.JudgingStarted, "during judging", EventStatus.CheckInClosed, "Start of judging canceled.");

    public Task<StatusMessage> EndJudgingAsync(Admin admin)
        => SetPhaseAsync(admin, EventStatus.JudgingStarted, "during judging", EventStatus.Finished, "Event finished.");

    public Task<StatusMessage> CancelJudgingEndAsync(Admin admin)
        => SetPhaseAsync(admin, EventStatus.Finished, "after judging", EventStatus.JudgingStarted, "Judging reopened.");

    private async Task<StatusMessage> SetPhaseAsync(Admin admin, EventStatus expected, string expectedText, EventStatus next, string nextText)
    {
        if (!admin.IsOwner)
        {
            return Error("This operation requires ownership rights.");
        }
        if (eventStatus.Value != expected)
        {
            return Error($"This operation is only valid {expectedText}.");
        }

        eventStatus.Set(next);
        return Success(nextText);
    }
}