using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class VisaInvitationLetterPageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsForbiddenWhenVisaInvitationsAreNotEnabled()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var page = GetPage(null);
        var view = await page.ViewAsync(await GetParticipantAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsForbiddenWhenCheckInHasStarted()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var page = GetPage(VisaInvitationFormat, status: EventStatus.CheckInStarted);
        var view = await page.ViewAsync(await GetParticipantAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsForbiddenWhenParticipantIsNotConfirmed()
    {
        var page = GetPage(VisaInvitationFormat);
        var view = await page.ViewAsync(await GetParticipantAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PageIsVisibleBeforeCheckInStartsWhenVisaInvitationsAreEnabled(bool requestedAlready)
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        if (requestedAlready)
        {
            var participant = await GetParticipantAsync();
            participant.VisaInformation.PassportPhotoId = "some-id";
            participant.VisaInformation.ParticipantDetails = [.. VisaInvitationFormat.ParticipantDetails.Select(_ => "x")];
            await Db.CommitAsync();
        }

        var page = GetPage(VisaInvitationFormat);
        var view = await page.ViewAsync(await GetParticipantAsync());
        Assert.IsFalse(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
        if (requestedAlready)
        {
            Assert.AreEqual("Manage", view.Action);
        }
        else
        {
            Assert.AreEqual("Request", view.Action);
        }
    }

    [TestMethod]
    public async Task RequestSetsPassportPhotoAndDetails()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            Db.Admins.Add(
                new Admin("admin@example.org")
            );
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "text/plain", [0, 1, 2, 3]);
        var details = VisaInvitationFormat.ParticipantDetails.Select(_ => "x").ToArray();
        {
            var result = await GetPage(VisaInvitationFormat, enableEmails: true).RequestAsync(await GetParticipantAsync(), file, details);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.IsNotNull(newParticipant.VisaInformation);
        Assert.IsNotNull(newParticipant.VisaInformation.PassportPhotoId);
        var storedFile = await FileStorage.GetFileAsync(newParticipant.VisaInformation.PassportPhotoId);
        Assert.IsNotNull(storedFile);
        Assert.AreEqual(file.MimeType, storedFile.MimeType);
        Assert.AreSequenceEqual(details, newParticipant.VisaInformation.ParticipantDetails);
    }

    [TestMethod]
    public async Task RequestCanOverwriteData()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            Db.Admins.Add(
                new Admin("admin@example.org")
            );
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "text/plain", [0, 1, 2, 3]);
        var details = VisaInvitationFormat.ParticipantDetails.Select(_ => "x").ToArray();
        {
            await GetPage(VisaInvitationFormat, enableEmails: true).RequestAsync(await GetParticipantAsync(), file, details);
            await Db.CommitAsync();
        }

        var file2 = new File.InMemory("name", "text/notplain", [7, 8, 9]);
        var details2 = VisaInvitationFormat.ParticipantDetails.Select(_ => "y").ToArray();
        {
            var result2 = await GetPage(VisaInvitationFormat, enableEmails: true).RequestAsync(await GetParticipantAsync(), file2, details2);
            Assert.AreEqual(Status.Success, result2.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await GetParticipantAsync();
        Assert.IsNotNull(newParticipant);
        Assert.IsNotNull(newParticipant.VisaInformation);
        Assert.IsNotNull(newParticipant.VisaInformation.PassportPhotoId);
        var storedFile = await FileStorage.GetFileAsync(newParticipant.VisaInformation.PassportPhotoId);
        Assert.IsNotNull(storedFile);
        Assert.AreEqual(file2.MimeType, storedFile.MimeType);
        Assert.AreSequenceEqual(details2, newParticipant.VisaInformation.ParticipantDetails);
    }

    [TestMethod]
    public async Task RequestFailsIfNotAvailable()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var file = new File.InMemory("name", "text/plain", [0, 1, 2, 3]);
        var details = VisaInvitationFormat.ParticipantDetails.Select(_ => "x").ToArray();
        var result = await GetPage(null).RequestAsync(await GetParticipantAsync(), file, details);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RequestFailsWithoutEnoughDetails()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var file = new File.InMemory("name", "text/plain", [0, 1, 2, 3]);
        var details = VisaInvitationFormat.ParticipantDetails
                                          .Skip(1)
                                          .Select(_ => "x").ToArray();
        var result = await GetPage(VisaInvitationFormat).RequestAsync(await GetParticipantAsync(), file, details);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RequestFailsWithTooManyDetails()
    {
        await SetParticipantStatusAsync(ParticipantStatus.Confirmed);

        var file = new File.InMemory("name", "text/plain", [0, 1, 2, 3]);
        var details = VisaInvitationFormat.ParticipantDetails.Select(_ => "x").Append("toomuch").ToArray();
        var result = await GetPage(VisaInvitationFormat).RequestAsync(await GetParticipantAsync(), file, details);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task RequestSendsEmailToAdmins()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.Confirmed;
            Db.Admins.Add(
                new Admin("admin@example.org")
            );
            await Db.CommitAsync();
        }

        var file = new File.InMemory("name", "text/plain", [0, 1, 2, 3]);
        var details = VisaInvitationFormat.ParticipantDetails.Select(_ => "x").ToArray();
        var result = await GetPage(VisaInvitationFormat, enableEmails: true).RequestAsync(await GetParticipantAsync(), file, details);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual("admin@example.org", email.Recipient);
        Assert.AreEqual(Operation.CreatePageView<Admin, VisaInvitationLettersPage>(), email.Operation);
    }

    private VisaInvitationLetterPage GetPage(VisaInvitationFormat? format, EventStatus status = EventStatus.ApplicationsOpen, bool enableEmails = false)
        => new(Db.Admins, format, status, FileStorage, enableEmails ? EmailSender : DisabledEmailSender);
}