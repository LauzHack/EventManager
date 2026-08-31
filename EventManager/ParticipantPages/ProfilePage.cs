using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class ProfilePage(ProfileForm profileForm, FileStorage fileStorage, EmailSender emailSender) : Page<Participant>
{
    public const string FileRemovalPrefix = "remove-";

    public override async Task<PageView> ViewAsync(Participant participant)
    {
        ImmutableArray<PageSummaryItem> CreateSummary()
        {
            var result = ImmutableArray.CreateBuilder<PageSummaryItem>();
            foreach (var choice in profileForm.Choices)
            {
                if (!choice.IsRequiredSingleOption && participant.Profile.TryGetValue(choice.Name, out var value))
                {
                    result.Add((choice.Name, value));
                }
            }
            foreach (var file in profileForm.Files)
            {
                if (participant.Profile.ContainsKey(file.Name))
                {
                    result.Add((file.Name, "provided"));
                }
            }
            return result.ToImmutable();
        }

        if (participant.Status >= ParticipantStatus.Finalized)
        {
            // The profile can be empty for manually-created participants at checkin, we don't want them to have to fill it
            if (participant.Profile.IsEmpty)
            {
                return ForbiddenView();
            }
            return SummaryOnlyView("Profile", CreateSummary());
        }
        if (profileForm.IsEmpty)
        {
            return ForbiddenView();
        }
        if (participant.Status == ParticipantStatus.ProfileFilled)
        {
            return EditableView("Profile", "Edit", CreateSummary());
        }
        return RequiredView("Profile");
    }

    public async Task<StatusMessage> EditAsync(Participant participant, OperationArguments values)
    {
        var (changedValues, missingValues) = await FillFormAsync(participant, values);

        var text = new StringBuilder();
        if (!changedValues.IsEmpty)
        {
            text.AppendLine("You updated your profile:");
            text.AppendJoin(Environment.NewLine, changedValues.Select(p => $"- {p.Key}: {p.Value}")).AppendLine();
        }
        if (missingValues.Any())
        {
            if (text.Length > 0)
            {
                text.AppendLine();
            }
            text.AppendLine("**You still need to fill in**:");
            text.AppendJoin(Environment.NewLine, missingValues.Select(v => $"- {v}")).AppendLine();
        }

        if (missingValues.Length > 0)
        {
            return Error(text.ToString());
        }

        participant.Status = ParticipantStatus.ProfileFilled;
        return Success(text.ToString());
    }

    public async Task<StatusMessage> WithdrawAsync(Participant participant)
    {
        if (participant.Status >= ParticipantStatus.ProfileFilled)
        {
            // we'd need to handle application groups
            return Error("Cannot use this withdraw function. How did you get here?");
        }

        if (participant.Status == ParticipantStatus.WithdrawnBeforeConfirmation)
        {
            return NoChange();
        }

        await emailSender.SendEmailAsync(
            recipient: participant.EmailAddress,
            subject: "Withdrawal",
            body: "You have withdrawn your application. If you would like to undo this, please use the link below.",
            operation: Operation.CreatePageAction<Participant?, WithdrawnPage>(nameof(WithdrawnPage.UndoAsync)),
            operationDescription: "Undo withdrawal"
        );

        participant.Status = ParticipantStatus.WithdrawnBeforeConfirmation;
        return Success("You have withdrawn.");
    }

    private async Task<(ImmutableArray<(string Key, string Value)> ChangedValues, ImmutableArray<string> MissingValues)> FillFormAsync(Participant participant, OperationArguments values)
    {
        var changedValues = ImmutableArray.CreateBuilder<(string, string)>();
        var missingValues = ImmutableArray.CreateBuilder<string>();

        foreach (var choice in profileForm.Choices)
        {
            // We don't really care about the UX of sending a bad answer to the backend since the frontend is supposed to prevent it, so just ignore bad answers silently
            if (values.TryGetText(choice.Name, out var answer) && choice.IsAcceptableAnswer(answer))
            {
                participant.Profile = participant.Profile.SetItem(choice.Name, answer);
                changedValues.Add((choice.Name, answer));
            }
            else if (choice.IsRequired && !participant.Profile.ContainsKey(choice.Name))
            {
                missingValues.Add(choice.Name);
            }
            else if (!choice.IsRequired)
            {
                participant.Profile = participant.Profile.Remove(choice.Name);
                changedValues.Add((choice.Name, "No"));
            }
        }

        var fileIdsToDelete = new List<string>();
        foreach (var file in profileForm.Files)
        {
            if (values.TryGetFile(file.Name, out var upload))
            {
                if (participant.Profile.TryGetValue(file.Name, out var existingId))
                {
                    fileIdsToDelete.Add(existingId);
                }
                var uploadedId = await fileStorage.StoreFileAsync(upload);
                participant.Profile = participant.Profile.SetItem(file.Name, uploadedId);
                changedValues.Add((file.Name, "updated"));
            }
            else if (values.TryGetText(FileRemovalPrefix + file.Name, out _) && participant.Profile.TryGetValue(file.Name, out var existingId))
            {
                await fileStorage.DeleteFileAsync(existingId);
                participant.Profile = participant.Profile.Remove(file.Name);
                changedValues.Add((file.Name, "removed"));
            }

            if (file.IsRequired && !participant.Profile.ContainsKey(file.Name))
            {
                missingValues.Add(file.Name);
            }
        }

        foreach (var fileId in fileIdsToDelete)
        {
            await fileStorage.DeleteFileAsync(fileId);
        }

        return (changedValues.ToImmutable(), missingValues.ToImmutable());
    }
}