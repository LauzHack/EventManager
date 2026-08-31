using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class DisabledPageTests : ParticipantTestsBase
{
    [TestMethod]
    [DataRow(EventStatus.Configuring)]
    [DataRow(EventStatus.ApplicationsClosed)]
    [DataRow(EventStatus.CheckInStarted)]
    public async Task PageIsRequiredWhenApplicationsAreNotOpenWithoutUser(EventStatus status)
    {
        var view = await new DisabledPage(Db.Participants, status, DisabledEmailSender).ViewAsync(null);

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation)]
    [DataRow(ParticipantStatus.Rejected)]
    [DataRow(ParticipantStatus.ProfileFilled)]
    [DataRow(ParticipantStatus.Finalized)]
    public async Task PageIsRequiredWhenApplicationsAreClosedWithNonAcceptedUser(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            await Db.CommitAsync();
        }

        var view = await new DisabledPage(Db.Participants, EventStatus.ApplicationsClosed, DisabledEmailSender).ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsHiddenWhenApplicationsAreOpen()
    {
        var view = await new DisabledPage(Db.Participants, EventStatus.ApplicationsOpen, DisabledEmailSender).ViewAsync(null);

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation)]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.CheckedIn)]
    public async Task PageIsHiddenWhenApplicationsAreClosedAndParticipantIsAtLeastAcceptedOrWithdrewAfterConfirmation(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            await Db.CommitAsync();
        }

        var view = await new DisabledPage(Db.Participants, EventStatus.ApplicationsClosed, DisabledEmailSender).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Accepted)]
    [DataRow(ParticipantStatus.Confirmed)]
    [DataRow(ParticipantStatus.WithdrawnAfterConfirmation)]
    public async Task LogInSendsEmailIfUserIsAccepted(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            await Db.CommitAsync();
        }

        var result = await new DisabledPage(Db.Participants, EventStatus.ApplicationsOpen, EmailSender).LogInAsync(ParticipantEmailAddress);

        Assert.AreEqual(Status.Success, result.Status);
        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, email.Recipient);
        Assert.AreEqual(Operation.CreatePageView<Participant>(), email.Operation);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Rejected)]
    [DataRow(ParticipantStatus.WithdrawnBeforeConfirmation)]
    public async Task LogInFailsIfUserIsNotAccepted(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            await Db.CommitAsync();
        }

        var result = await new DisabledPage(Db.Participants, EventStatus.ApplicationsOpen, DisabledEmailSender).LogInAsync(ParticipantEmailAddress);

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task LogInFailsIfUserDoesNotExist()
    {
        var result = await new DisabledPage(Db.Participants, EventStatus.ApplicationsOpen, DisabledEmailSender).LogInAsync("doesnotexist@example.org");

        Assert.AreEqual(Status.UserError, result.Status);
    }
}