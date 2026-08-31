using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ChallengeSetterPages;

public sealed class DescriptionPage(EventStatus eventStatus) : Page<ChallengeSetter>
{
    public override async Task<PageView> ViewAsync(ChallengeSetter setter)
    {
        if (setter.Description is null || eventStatus < EventStatus.CheckInStarted)
        {
            return RequiredView("Challenge description");
        }
        return EditableView("Challenge description", "Edit", ("The description is now visible to participants.", ""));
    }

    public async Task<StatusMessage> EditAsync(ChallengeSetter setter, string description)
    {
        if (description.Length > ChallengeSetter.MaxDescriptionLength)
        {
            return Error($"The description must be at most {ChallengeSetter.MaxDescriptionLength} characters long.");
        }

        setter.Description = description;
        return Success("Description updated.");
    }
}