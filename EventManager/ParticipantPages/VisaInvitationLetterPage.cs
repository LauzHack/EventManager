using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class VisaInvitationLetterPage(IQueryable<Admin> admins,
                                             VisaInvitationFormat? visaInvitationFormat, EventStatus eventStatus,
                                             FileStorage fileStorage, EmailSender emailSender) : Page<Participant>
{
    public override async Task<PageView> ViewAsync(Participant participant)
    {
        if (visaInvitationFormat is null || eventStatus >= EventStatus.CheckInStarted || participant.Status < ParticipantStatus.Confirmed)
        {
            return ForbiddenView();
        }
        string action = participant.VisaInformation.PassportPhotoId is null ? "Request" : "Manage";
        return EditableView("Visa invitation letter", action);
    }

    public async Task<StatusMessage> RequestAsync(Participant participant, File passport, string[] details)
    {
        if (visaInvitationFormat is null)
        {
            return Error("Visa invitation requests are not available, how did you get here?");
        }
        if (details.Length != visaInvitationFormat.ParticipantDetails.Length)
        {
            return Error("Please provide all of the necessary details");
        }

        var adminEmailAddresses = await admins.Select(a => a.EmailAddress).ToCollectionAsync();
        await emailSender.SendAsync([.. adminEmailAddresses.Select(emailAddress => new Email(
            Recipient: emailAddress,
            Subject: "Visa invitation letter required",
            Body: $"{participant.FullName} requested a visa invitation letter.",
            Operation: Operation.CreatePageView<Admin, VisaInvitationLettersPage>(),
            OperationDescription: "Check it"
        ))]);

        var passportId = await fileStorage.StoreFileAsync(passport);
        if (participant.VisaInformation.PassportPhotoId is string existingId)
        {
            await fileStorage.DeleteFileAsync(existingId);
        }

        participant.VisaInformation.PassportPhotoId = passportId;
        participant.VisaInformation.ParticipantDetails = details;

        return Success($"Your request has been created. Please wait a few days for the organizers to process it.");
    }
}