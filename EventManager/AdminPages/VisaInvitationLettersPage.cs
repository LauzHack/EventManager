using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class VisaInvitationLettersPage(DbValues<Participant> participants,
                                              LetterData? letterData, ConfigValue<VisaInvitationFormat> inviteFormat,
                                              EmailSender emailSender, TimeProvider timeProvider) : Page<Admin>
{
    public override bool RedisplayAfterAction
        => true;

    public override async Task<PageView> ViewAsync(Admin admin)
    {
        const string Title = "Visa invitation letters";

        if (letterData is null)
        {
            return SummaryOnlyView(Title, ("Disabled, letters not enabled", ""));
        }

        if (inviteFormat.Value is null)
        {
            return EditableView(Title, "Set up");
        }

        var openRequestsCount = await participants.CountAsync(p => p.VisaInformation.PassportPhotoId != null && p.VisaInformation.Letter == null);
        if (openRequestsCount > 0)
        {
            return EditableView(Title, "Handle", ("Unhandled requests", openRequestsCount));
        }

        return EditableView(Title, "Edit");
    }

    public override async Task<object?> GetModelAsync(Admin admin)
        => await participants.Where(p => p.VisaInformation.PassportPhotoId != null)
                             .OrderBy(p => p.VisaInformation.Letter != null)
                             .ThenByName()
                             .ToCollectionAsync();

    public async Task<StatusMessage> SetFormatAsync(Admin admin, VisaInvitationFormat format)
    {
        if (!admin.IsOwner)
        {
            return Error("Only an owner can set the visa invitation letter template.");
        }
        if (!format.ContainsPlaceholders)
        {
            return Error("The template should contain placeholders.");
        }

        inviteFormat.Set(format);
        return Success("Visa invitation letter format updated.");
    }

    public async Task<StatusMessage> AcceptAsync(string emailAddress, string details)
    {
        if (inviteFormat.Value is null)
        {
            return Error("No visa invitation letter template configured.");
        }

        var participant = await participants.FindAsync(emailAddress);
        if (participant is null)
        {
            return Error($"No participant with email **{emailAddress}**.");
        }
        if (participant.FullName is null)
        {
            return Error($"Participant **{participant.EmailAddress}** has not filled in their name yet.");
        }
        if (participant.VisaInformation.PassportPhotoId is null)
        {
            return Error($"Participant **{participant.FullName}** did not request a visa invitation letter.");
        }

        var id = DeterministicId.Create(participant.EmailAddress, timeProvider);
        var body = inviteFormat.Value.CreateLetterBody(participant.FullName, details);

        participant.VisaInformation.AdminDetails = details;
        participant.VisaInformation.Letter = new Letter(id, body, timeProvider.GetUtcNow());

        await emailSender.SendEmailAsync(
            recipient: participant.EmailAddress,
            subject: "Visa invitation letter",
            body: "Your visa invitation letter is now available.",
            operation: Operation.CreateLetterView(participant.VisaInformation.Letter.Id),
            operationDescription: "View"
        );

        return Success($"Created a letter and notified **{participant.FullName}**.");
    }

    public async Task<StatusMessage> RejectAsync(string emailAddress, string reason)
    {
        var participant = await participants.FindAsync(emailAddress);
        if (participant is null)
        {
            return Error($"No participant with email **{emailAddress}**.");
        }
        if (participant.VisaInformation.PassportPhotoId is null)
        {
            return Error($"Participant **{participant.FullName}** did not request a visa invitation letter.");
        }

        participant.VisaInformation.PassportPhotoId = null;

        await emailSender.SendEmailAsync(
            recipient: participant.EmailAddress,
            subject: "About your visa invitation letter request",
            body: $"Your request was rejected because: {reason}. Please submit a new, corrected request.",
            operation: Operation.CreatePageView<Participant>()
        );

        return Success($"Rejected the request and notified **{participant.FullName}**.");
    }
}