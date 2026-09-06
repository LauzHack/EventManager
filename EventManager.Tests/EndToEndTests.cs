using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.ParticipantPages;
using EventManager.Tests.TestInfrastructure;
using EventManager.Web;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests;

[TestClass]
public sealed class EndToEndTests
{
    [TestMethod]
    public async Task RequestWithNullUriIsNotHandled()
    {
        using var system = new TestEventManagerSystem();
        await system.ExecuteRequestAsync<OperationResult.NotFound>([], null);

        // It's OK if an external timer triggers the periodic tasks at this point
        await system.RunPeriodicTasksAsync();
    }

    [TestMethod]
    public async Task CrashWhenGettingUriResultsInSystemErrorResponse()
    {
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: false);

        system.CrashWhenGettingUri = true;
        await system.ExecuteRequestAsync<OperationResult.SystemError>(adminStorage, "/");
    }

    [TestMethod]
    public async Task CrashWhenCreatingDatabaseResultsInSystemErrorResponse()
    {
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: false);

        system.CrashWhenCreatingDatabase = true;
        await system.ExecuteRequestAsync<OperationResult.SystemError>(adminStorage, "/");
    }

    [TestMethod]
    public async Task CrashOnDatabaseCommitResultsInSystemErrorResponse()
    {
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: false);

        system.UseCrashyDatabase = true;
        await system.ExecuteRequestAsync<OperationResult.SystemError>(adminStorage, "/");
    }

    [TestMethod]
    public async Task CrashWhenCreatingTimeProviderResultsInSystemErrorResponse()
    {
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: false);

        system.CrashWhenCreatingTimeProvider = true;
        await system.ExecuteRequestAsync<OperationResult.SystemError>(adminStorage, "/");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task CrashWhenCreatingEmailSenderResultsInSystemErrorResponse(int mode)
    {
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: false);

        if (mode == 1)
        {
            system.CrashWhenCreatingEmailSenderWithIgnoredException = true;
        }
        else if (mode == 2)
        {
            system.CrashWhenCreatingEmailSenderWithOperationCanceledException = true;
        }
        else
        {
            system.CrashWhenCreatingEmailSender = true;
        }

        await system.ExecuteRequestAsync<OperationResult.SystemError>(adminStorage, "/");

        system.CrashWhenCreatingEmailSender = false;
        system.CrashWhenCreatingEmailSenderWithIgnoredException = false;
        system.CrashWhenCreatingEmailSenderWithOperationCanceledException = false;
        var auditPage = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Audit");
        var logs = Assert.IsInstanceOfType<IReadOnlyCollection<AuditMessage>>(auditPage.Model);
        Assert.HasCount(mode == 0 ? 1 : 0, logs.Where(l => l.Status == Status.SystemError));
    }

    [TestMethod]
    public async Task AdminConfig()
    {
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: false);

        // At this point participants can't do anything
        var participantResponse = await system.ExecuteRequestAsync<OperationResult.Page>([], "/");
        Assert.IsInstanceOfType<DisabledPage>(participantResponse.View.Page);
        await system.ExecuteRequestAsync<OperationResult.Unavailable>([], "/Participant/Email/Edit", ("email", "alice@example.org"));

        // Invalid URIs should be properly handled
        await system.ExecuteRequestAsync<OperationResult.NotFound>(adminStorage, "/Admin/PageThatDefinitelyDoesNotExistAndNeverWill");
        await system.ExecuteRequestAsync<OperationResult.NotFound>(adminStorage, "/Admin/EmailSetup/MethodThatDefinitelyDoesNotExistAndNeverWill");
        await system.ExecuteRequestAsync<OperationResult.NotFound>(adminStorage, "/Admin/Too/Many/Segments/PageThatDefinitelyDoesNotExistAndNeverWill");
        await system.ExecuteRequestAsync<OperationResult.NotFound>([], "/UserTypeThatDefinitelyDoesNotExistAndNeverWill");

        // No letters nor files
        await system.ExecuteRequestAsync<OperationResult.NotFound>([], "/Letter/some_id");
        await system.ExecuteRequestAsync<OperationResult.NotFound>([], "/File/some_id");
    }

    [TestMethod]
    public async Task SingleParticipantApplication()
    {
        var participantStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();

        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, participantStorage);

        // A participant cannot access the admin panel
        await system.ExecuteRequestAsync<OperationResult.AuthenticationRequired>(participantStorage, "/Admin/Audit");

        // A malicious participant cannot take control of the account even knowing its email address
        await system.ExecuteRequestAndAssertSuccessAsync(
            [], "/Participant/Email/Edit",
            ("emailAddress", "evil@example.org")
        );
        var (evilLoginEmail, _, evilAuthSecret) = system.DequeueEmail(assertSingle: true);
        Assert.IsNotNull(evilLoginEmail.Operation);
        var op = Authenticator.AddAuthentication(evilAuthSecret, evilLoginEmail.Operation, "evil@example.org");
        var evilPage = await system.ExecuteRequestAsync<OperationResult.Page>(
            [], "/Participant/Email/ChangeEmailAddress",
            op.Arguments + OperationArguments.FromPairs(("oldEmailAddress", "alice@example.org"))
        );
        Assert.AreEqual(Status.UserError, evilPage.Status);
    }

    [TestMethod]
    public async Task SingleParticipantApplicationWithReminder()
    {
        var participantStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();

        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, participantStorage, confirm: false);

        // Time flies!
        system.TimeProvider.FixedDate = system.TimeProvider.FixedDate!.Value.AddDays(5).AddSeconds(1);
        await system.RunPeriodicTasksAsync();

        // The participant should have received a reminder
        var (reminderEmail, _, _) = system.DequeueEmail(assertSingle: true);
        Assert.AreEqual("alice@example.org", reminderEmail.Recipient);
        Assert.AreEqual("Reminder to confirm", reminderEmail.Subject);
    }

    [TestMethod]
    public async Task SingleParticipantApplicationDoesNotConfirmInTime()
    {
        var participantStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();

        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, participantStorage, confirm: false);

        // Time flies!
        system.TimeProvider.FixedDate = system.TimeProvider.FixedDate!.Value.AddDays(200);
        await system.RunPeriodicTasksAsync();

        // The participant should have been rejected
        var (rejectionEmail, _, _) = system.DequeueEmail(assertSingle: true);
        Assert.AreEqual("alice@example.org", rejectionEmail.Recipient);
        Assert.AreEqual("You did not confirm in time", rejectionEmail.Subject);

        // Admins should see this
        var participantsPage = await system.ExecuteRequestAsync<OperationResult.Page>(adminStorage, "/Admin/Participants");
        var participants = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(participantsPage.Model);
        Assert.AreEqual(ParticipantStatus.DidNotConfirm, participants.Single().Status);
        Assert.AreSequenceEqual([("Did not confirm in time", "1")], participantsPage.View.Summary);
    }

    [TestMethod]
    public async Task SingleParticipantApplicationThenWithdrawalThenBackIn()
    {
        var participantStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();

        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, participantStorage);

        // The participant decides to withdraw,
        var withdrawalPage = await system.ExecuteRequestAsync<OperationResult.Page>(participantStorage, "/Participant/Travel/Withdraw");
        Assert.IsInstanceOfType<WithdrawnPage>(withdrawalPage.View.Page);

        // then admins close applications,
        var closedPage = await system.ExecuteRequestAsync<OperationResult.Page>(adminStorage, "/Admin/Acceptance/Close");
        Assert.IsInstanceOfType<StartCheckInPage>(closedPage.View.Page);

        // then the participant changes their mind.
        // This is a regression test, page order used to be wrong in a way that would prevent this from working.
        var travelPage = await system.ExecuteRequestAsync<OperationResult.Page>(participantStorage, "/Participant/Withdrawn/Undo");
        Assert.IsInstanceOfType<TravelPage>(travelPage.View.Page);
    }

    [TestMethod]
    public async Task SingleParticipantApplicationConfirmingAfterEventStarts()
    {
        var participantStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();

        // A participant applies and is accepted, but doesn't confirm yet.
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true, projectTeamSize: 4);
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, participantStorage, confirm: false);

        // The event starts...
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Acceptance/Close");
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/StartCheckIn/Start");

        // and then the participant confirms.
        var result = await system.ExecuteRequestAndAssertSuccessAsync(participantStorage, "/Participant/WaitForAcceptance/Confirm");
        // At this point the participant should be on the "wait for check in" page.
        Assert.IsInstanceOfType<WaitForCheckInPage>(result.View.Page);
        // Then they can check in...
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/CheckIn/CheckIn",
            ("emailAddress", "alice@example.org")
        );
        // ...and now the participant has caught up and is at the projects page.
        var result2 = await system.ExecuteRequestAndAssertSuccessAsync(participantStorage, "/Participant");
        Assert.IsInstanceOfType<ProjectPage>(result2.View.Page);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GroupApplication(bool usingDifferentEmailForInvitation)
    {
        var aliceStorage = new Dictionary<string, string>();
        var bobStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();

        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);

        // First, Alice signs up.
        // She fills her email,
        await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/Email/Edit",
            ("emailAddress", "alice@example.org")
        );
        var (aliceLoginEmail, _, aliceLoginAuthSecret) = system.DequeueEmail(assertSingle: true);
        Assert.IsNotNull(aliceLoginEmail.Operation);
        var aliceEmailOperation = Authenticator.AddAuthentication(aliceLoginAuthSecret, aliceLoginEmail.Operation, aliceLoginEmail.Recipient);
        await system.ExecuteRequestAsync<OperationResult.Page>(aliceStorage, aliceEmailOperation.RelativeUri.ToString());
        // her name,
        await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/Name/Edit",
            ("givenName", "Alice"),
            ("familyName", "Apple")
        );
        // and her profile.
        await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/Profile/Edit",
            ("Choice One", "B"),
            ("Choice Two", "XYZ")
        );

        // Then, her friend Bob signs up.
        // He fills his email,
        await system.ExecuteRequestAndAssertSuccessAsync(
            bobStorage, "/Participant/Email/Edit",
            ("emailAddress", "bob@example.org")
        );
        var (bobLoginEmail, _, bobLoginAuthSecret) = system.DequeueEmail(assertSingle: true);
        Assert.IsNotNull(bobLoginEmail.Operation);
        Assert.AreSequenceEqual(aliceLoginAuthSecret.HashKey, bobLoginAuthSecret.HashKey);
        var bobEmailOperation = Authenticator.AddAuthentication(bobLoginAuthSecret, bobLoginEmail.Operation, bobLoginEmail.Recipient);
        await system.ExecuteRequestAsync<OperationResult.Page>(bobStorage, bobEmailOperation.RelativeUri.ToString());
        // his name,
        await system.ExecuteRequestAndAssertSuccessAsync(
            bobStorage, "/Participant/Name/Edit",
            ("givenName", "Bob"),
            ("familyName", "Banana")
        );
        // and his profile.
        await system.ExecuteRequestAndAssertSuccessAsync(
            bobStorage, "/Participant/Profile/Edit",
            ("Choice One", "A"),
            ("Choice Two", "ZZZ")
        );

        // Bob wants to invite Alice.
        if (usingDifferentEmailForInvitation)
        {
            // Variant 1: He uses a different email by accident.
            await system.ExecuteRequestAndAssertSuccessAsync(
                bobStorage, "/Participant/Group/CreateInvitation",
                ("emailAddress", "31337h4x0r@example.org")
            );
            // Alice clicks on that email to accept,
            var (alice2LoginEmail, _, alice2LoginAuthSecret) = system.DequeueEmail(assertSingle: true);
            Assert.AreSequenceEqual(aliceLoginAuthSecret.HashKey, alice2LoginAuthSecret.HashKey);
            Assert.IsNotNull(alice2LoginEmail.Operation);
            var alice2EmailOperation = Authenticator.AddAuthentication(alice2LoginAuthSecret, alice2LoginEmail.Operation, alice2LoginEmail.Recipient);
            await system.ExecuteRequestAndAssertSuccessAsync(aliceStorage, alice2EmailOperation.RelativeUri.ToString());
            // fills her name again, somewhat annoyed, and mistakenly swaps her given and family names in the process.
            var aliasResponse = await system.ExecuteRequestAndAssertSuccessAsync(
                aliceStorage, "/Participant/Name/Edit",
                ("givenName", "Apple"),
                ("familyName", "Alice")
            );
            // But she's impressed that the system recognizes her and asks her if she wants to use her existing account
            Assert.IsInstanceOfType<AliasPage>(aliasResponse.View.Page);
            // She chooses to do so,
            await system.ExecuteRequestAndAssertSuccessAsync(
                aliceStorage, "/Participant/Alias/ChooseCandidate",
                ("emailAddress", "alice@example.org")
            );
            // and confirms this by email to her original address.
            var (alice3LoginEmail, _, alice3LoginAuthSecret) = system.DequeueEmail(assertSingle: true);
            Assert.AreSequenceEqual(aliceLoginAuthSecret.HashKey, alice3LoginAuthSecret.HashKey);
            Assert.AreEqual("alice@example.org", alice3LoginEmail.Recipient);
            Assert.IsNotNull(alice3LoginEmail.Operation);
            var alice3EmailOperation = Authenticator.AddAuthentication(alice3LoginAuthSecret, alice3LoginEmail.Operation, alice3LoginEmail.Recipient);
            await system.ExecuteRequestAndAssertSuccessAsync(aliceStorage, alice3EmailOperation.RelativeUri.ToString());
        }
        else
        {
            // Variant 2: He uses the same email Alice applied with.
            await system.ExecuteRequestAndAssertSuccessAsync(
                bobStorage, "/Participant/Group/CreateInvitation",
                ("emailAddress", "alice@example.org")
            );
            // She clicks on the email to accept,
            var (aliceInviteEmail, _, aliceInviteAuthSecret) = system.DequeueEmail(assertSingle: true);
            Assert.AreSequenceEqual(aliceLoginAuthSecret.HashKey, aliceInviteAuthSecret.HashKey);
            Assert.IsNotNull(aliceInviteEmail.Operation);
            var aliceInviteEmailOperation = Authenticator.AddAuthentication(aliceInviteAuthSecret, aliceInviteEmail.Operation, aliceInviteEmail.Recipient);
            await system.ExecuteRequestAndAssertSuccessAsync(aliceStorage, aliceInviteEmailOperation.RelativeUri.ToString());
        }

        var bobGroupPage = await system.ExecuteRequestAndAssertSuccessAsync(bobStorage, "/");
        var bobGroupPageModel = Assert.IsInstanceOfType<GroupPage.Model>(bobGroupPage.Model);
        Assert.IsNotNull(bobGroupPageModel.Group);

        // Alice then accepts the invitation.
        await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/Group/AcceptInvitation",
            ("id", bobGroupPageModel.Group.Id)
        );

        // Finally, Alice finalizes the group's application.
        await system.ExecuteRequestAndAssertSuccessAsync(aliceStorage, "/Participant/Group/Finalize");
        var (finalizeEmail1, _, _) = system.DequeueEmail(assertSingle: false);
        var (finalizeEmail2, _, _) = system.DequeueEmail(assertSingle: true);
        Assert.AreSequenceEqual(["alice@example.org", "bob@example.org"], [finalizeEmail1.Recipient, finalizeEmail2.Recipient], SequenceOrder.InAnyOrder);

        // An admin, who is a friend of Bob, decides to accept Bob's group. (Nepotism? In MY end-to-end tests? It's more likely than you think.)
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/Acceptance/AcceptSpecific",
            ("emailAddress", "bob@example.org"),
            ("givenName", "Bob"),
            ("familyName", "Banana")
        );

        // Each of them can now confirm,
        var (confirmEmail1, _, _) = system.DequeueEmail(assertSingle: false);
        var (confirmEmail2, _, _) = system.DequeueEmail(assertSingle: true);
        Assert.IsNotNull(confirmEmail1.Operation);
        var confirmEmail1Operation = Authenticator.AddAuthentication(aliceLoginAuthSecret, confirmEmail1.Operation, confirmEmail1.Recipient);
        await system.ExecuteRequestAsync<OperationResult.Page>([], confirmEmail1Operation.RelativeUri.ToString());
        Assert.IsNotNull(confirmEmail2.Operation);
        var confirmEmail2Operation = Authenticator.AddAuthentication(aliceLoginAuthSecret, confirmEmail2.Operation, confirmEmail2.Recipient);
        await system.ExecuteRequestAsync<OperationResult.Page>([], confirmEmail2Operation.RelativeUri.ToString());
        // and receives an email indicating they confirmed.
        system.DequeueEmail(assertSingle: false);
        system.DequeueEmail(assertSingle: true);

        var participantsPage = await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/Participants"
        );
        var participants = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(participantsPage.Model);
        var alice = participants.FirstOrDefault(p => p.EmailAddress.Equals("alice@example.org", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(alice);
        Assert.AreEqual(ParticipantStatus.Confirmed, alice.Status);
        var bob = participants.FirstOrDefault(p => p.EmailAddress.Equals("bob@example.org", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(bob);
        Assert.AreEqual(ParticipantStatus.Confirmed, bob.Status);
    }

    [TestMethod]
    public async Task SingleParticipantWithVisaInvitationLetter()
    {
        var participantStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();

        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);

        // The admin configures letters and visa invite letters
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/LetterData/Edit",
            OperationArguments.FromPairs(("address", "Somewhere"), ("cultureName", "en-US"), ("signee", "Someone"), ("contact", "someone@example.org"))
                              .WithFile("signature", new File.InMemory("name", "image/png", [0]))
        );
        var formatResponse = await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/VisaInvitationLetters/SetFormat",
            ("format.Template", "Letter for $NAME: $DETAILS."),
            ("format.ParticipantDetails", "home address"),
            ("format.ParticipantDetails", "phone number"),
            ("format.AdminDetails", "Full name, country of birth, phone number, home address")
        );
        // The admin can easily go back to the "open applications" page
        Assert.IsTrue(formatResponse.AvailableViews.Any(v => v.Page is OpenApplicationsPage));

        // The participant applies, is accepted, and requests a letter.
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, participantStorage);
        await system.ExecuteRequestAndAssertSuccessAsync(
            participantStorage, "/Participant/VisaInvitationLetter/Request",
            OperationArguments.FromPairs(("details", "my home"), ("details", "555-12345"))
                              .WithFile("passport", new File.InMemory("name", "application/pdf", [0]))
        );

        // The admin is notified,
        var (adminNotifEmail, _, _) = system.DequeueEmail(assertSingle: true);
        Assert.AreEqual("admin@example.org", adminNotifEmail.Recipient);
        Assert.IsNotNull(adminNotifEmail.Operation);
        // can view the file,
        var invitationsPage = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, adminNotifEmail.Operation.RelativeUri.ToString());
        var participants = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(invitationsPage.Model);
        var participant = Assert.ContainsSingle(participants);
        Assert.IsNotNull(participant.VisaInformation.PassportPhotoId);
        var file = await system.ExecuteRequestAsync<OperationResult.File>(adminStorage, "/File/" + participant.VisaInformation.PassportPhotoId);
        Assert.IsNotEmpty(file.RequestedFile.Name);
        Assert.AreEqual("application/pdf", file.RequestedFile.MimeType);
        // and accepts the request
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/VisaInvitationLetters/Accept",
            ("emailAddress", "alice@example.org"),
            ("details", "Alice from Borginia, phone number 555-12345, home address 42 Lablanc Street")
        );

        // The participant is notified and can view it
        var (participantNotifEmail, _, _) = system.DequeueEmail(assertSingle: true);
        Assert.AreEqual("alice@example.org", participantNotifEmail.Recipient);
        Assert.IsNotNull(participantNotifEmail.Operation);
        var letter = await system.ExecuteRequestAsync<OperationResult.Letter>(participantStorage, participantNotifEmail.Operation.RelativeUri.ToString());
        Assert.Contains("Alice from Borginia", letter.Body, StringComparison.Ordinal);

        // But still, invalid letter IDs are invalid
        await system.ExecuteRequestAsync<OperationResult.NotFound>([], "/Letter/does-not-exist");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TwoParticipantsApplyIncludingTravelReimbursementAndGoThroughEvent(bool projectsEnabled)
    {
        var aliceStorage = new Dictionary<string, string>();
        var bobStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();

        // The system is set up,
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: false, projectTeamSize: projectsEnabled ? 4u : 0u);
        // including travel reimbursement,
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/TravelReimbursementPolicy/Edit",
            ("policy.EventCurrencyCode", "CHF"),
            ("policy.TiersDescription", "We reimburse your travel for this event based on location"),
            ("policy.DetailsUrl", "https://example.org"),
            ("policy.Tiers.Key", "A"),
            ("policy.Tiers.Key", "B"),
            ("policy.Tiers.Key", "X"),
            ("policy.Tiers.Value", "42"),
            ("policy.Tiers.Value", "100.5"),
            ("policy.Tiers.Value", "200"),
            ("policy.RoundingAmount", "0")
        );
        // and applications open.
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/OpenApplications/Open");

        // Alice and Bob apply, separately.
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, aliceStorage, givenName: "Alice", emailAddress: "alice@example.org", confirm: true);
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, bobStorage, givenName: "Bob", emailAddress: "bob@example.org", confirm: true);

        // Alice submits a travel expense,
        await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/Travel/ChooseTravelReimbursementTier",
            ("tier", "B")
        );
        await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/Travel/SubmitTravelExpense",
            OperationArguments.FromPairs(("description", "Train ticket"),
                                         ("amount", "40"),
                                         ("currencyCode", "CHF"),
                                         ("countsDouble", "false"))
                              .WithFile("receipt", new File.InMemory("name", "image/png", [0]))
        );
        var expensesPage1 = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/TravelExpenses");
        var expenses1 = Assert.IsInstanceOfType<IReadOnlyCollection<TravelExpensesPage.ExpenseWithContext>>(expensesPage1.Model);
        var expense1 = Assert.ContainsSingle(expenses1);
        // and it gets approved.
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/TravelExpenses/Approve",
            ("receiptId", expense1.ReceiptId),
            ("currencyExchangeRate", "1.5")
        );

        // Applications close.
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Acceptance/Close");

        // Bob submits a travel expense,
        await system.ExecuteRequestAndAssertSuccessAsync(
            bobStorage, "/Participant/Travel/ChooseTravelReimbursementTier",
            ("tier", "X")
        );
        await system.ExecuteRequestAndAssertSuccessAsync(
            bobStorage, "/Participant/Travel/SubmitTravelExpense",
            OperationArguments.FromPairs(("description", "Train ticket"),
                                         ("amount", "200"),
                                         ("currencyCode", "EUR"),
                                         ("countsDouble", "false"))
                              .WithFile("receipt", new File.InMemory("name", "image/png", [0]))
        );
        var expensesPage2 = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/TravelExpenses");
        var expenses2 = Assert.IsInstanceOfType<IReadOnlyCollection<TravelExpensesPage.ExpenseWithContext>>(expensesPage2.Model);
        var expense2 = Assert.ContainsSingle(expenses2.Where(e => e.Status is TravelExpenseStatus.Submitted));
        // but it gets rejected.
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/TravelExpenses/Reject",
            ("receiptId", expense2.ReceiptId),
            ("reason", "The receipt does not show the amount spent")
        );

        var overallChallengeStorage = new Dictionary<string, string>();
        var companyChallengeStorage = new Dictionary<string, string>();
        var company2ChallengeStorage = new Dictionary<string, string>();
        if (projectsEnabled)
        {
            // Admins create challenge setters,
            await system.ExecuteRequestAndAssertSuccessAsync(
                adminStorage, "/Admin/Challenges/Edit",
                ("setters.Name", "Overall"),
                ("setters.IsChallengeOptIn", "false"),
                ("setters.Name", "Some Company"),
                ("setters.IsChallengeOptIn", "true"),
                ("setters.Name", "Yet Another Company"),
                ("setters.IsChallengeOptIn", "true")
            );
            // which set their challenges.
            var overallLoginLink = await system.GetChallengeSetterLoginRelativeUriAsync("Overall");
            await system.ExecuteRequestAndAssertSuccessAsync(overallChallengeStorage, overallLoginLink);
            await system.ExecuteRequestAndAssertSuccessAsync(overallChallengeStorage, "/ChallengeSetter/Description/Edit",
                ("description", "The overall challenge description.")
            );
            var companyLoginLink = await system.GetChallengeSetterLoginRelativeUriAsync("Some Company");
            await system.ExecuteRequestAndAssertSuccessAsync(companyChallengeStorage, companyLoginLink);
            await system.ExecuteRequestAndAssertSuccessAsync(companyChallengeStorage, "/ChallengeSetter/Description/Edit",
                ("description", "Company's Awesome Challenge!")
            );
            var company2LoginLink = await system.GetChallengeSetterLoginRelativeUriAsync("Yet Another Company");
            await system.ExecuteRequestAndAssertSuccessAsync(company2ChallengeStorage, company2LoginLink);
            await system.ExecuteRequestAndAssertSuccessAsync(company2ChallengeStorage, "/ChallengeSetter/Description/Edit",
                ("description", "Boring challenge")
            );
        }

        // Challenges are not visible yet.
        await system.ExecuteRequestAsync<OperationResult.Unavailable>([], "/Challenges");

        // Check in starts.
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/StartCheckIn/Start");

        if (projectsEnabled)
        {
            // Now challenges are visible.
            var challenges = await system.ExecuteRequestAsync<OperationResult.Challenges>([], "/Challenges");
            Assert.AreSequenceEqual(
                ["Overall|The overall challenge description.", "Some Company|Company's Awesome Challenge!", "Yet Another Company|Boring challenge"],
                challenges.Setters.Select(s => $"{s.Name}|{s.Description}"), StringComparer.Ordinal
            );
        }

        // Alice checks in.
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/CheckIn/CheckIn",
            ("emailAddress", "alice@example.org")
        );

        // Check in ends.
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/CheckIn/FinishCheckIn");

        // Bob is late but checks in anyway.
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/CheckIn/CheckIn",
            ("emailAddress", "bob@example.org")
        );

        // Alice submits a travel expense with Bob,
        await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/Travel/SubmitTravelExpense",
            OperationArguments.FromPairs(("description", "Bus ticket"),
                                         ("amount", "6"),
                                         ("currencyCode", "CHF"),
                                         ("countsDouble", "false"),
                                         ("ownerEmailAddresses", "bob@example.org"))
                              .WithFile("receipt", new File.InMemory("name", "image/png", [0]))
        );
        var expensesPage3 = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/TravelExpenses");
        var expenses3 = Assert.IsInstanceOfType<IReadOnlyCollection<TravelExpensesPage.ExpenseWithContext>>(expensesPage3.Model);
        var expense3 = Assert.ContainsSingle(expenses3.Where(e => e.Status is TravelExpenseStatus.Submitted));
        // and it gets approved.
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/TravelExpenses/Approve",
            ("receiptId", expense3.ReceiptId),
            ("currencyExchangeRate", "1.5")
        );

        // Alice and Bob are done with expenses.
        await system.ExecuteRequestAndAssertSuccessAsync(aliceStorage, "/Participant/Travel/FinishDeclaringTravelExpenses");
        await system.ExecuteRequestAndAssertSuccessAsync(bobStorage, "/Participant/Travel/FinishDeclaringTravelExpenses");

        if (!projectsEnabled)
        {
            // There is nothing more to do, Alice and Bob both see the "welcome to the event" page
            var aliceResponse = await system.ExecuteRequestAndAssertSuccessAsync(aliceStorage, "/", OperationArguments.Empty);
            Assert.IsInstanceOfType<WelcomePage>(aliceResponse.View.Page);
            var bobResponse = await system.ExecuteRequestAndAssertSuccessAsync(aliceStorage, "/", OperationArguments.Empty);
            Assert.IsInstanceOfType<WelcomePage>(bobResponse.View.Page);
            return;
        }

        // Alice submits a project, selecting both company challenges (but not having to explicitly select the non-opt-in one)
        await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/Project/Edit",
            OperationArguments.FromPairs(("title", "FriendFinder"),
                                         ("shortDescription", "Find friends around you"),
                                         ("longDescription", "Make new friends who share the same interests"),
                                         ("link", "https://example.org/friend-finder"),
                                         ("challenges", "Some Company"),
                                         ("challenges", "Yet Another Company"))
                              .WithFile("thumbnail", new File.InMemory("name", "image/jpg", [0]))
        );
        // and invites Bob,
        var aliceProjectPage = await system.ExecuteRequestAndAssertSuccessAsync(
            aliceStorage, "/Participant/ProjectTeam/Invite",
            ("emailAddress", "bob@example.org")
        );
        var aliceProjectPageModel = Assert.IsInstanceOfType<ProjectTeamPage.Model>(aliceProjectPage.Model);
        Assert.IsNotNull(aliceProjectPageModel.Project);
        // who accepts.
        await system.ExecuteRequestAndAssertSuccessAsync(
            bobStorage, "/Participant/Project/Join",
            ("id", aliceProjectPageModel.Project.Id)
        );

        // The event enters its judging phase.
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/End/StartJudging");

        // Alice and Bob demo their project, and an admin marks them as such.
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/Projects/MarkAsDemoed",
            ("id", aliceProjectPageModel.Project.Id)
        );

        // Alice and Bob are both marked as having demoed in the travel reimbursement page,
        var reimbursementPage = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/TravelReimbursement");
        var reimbursements = Assert.IsInstanceOfType<IReadOnlyCollection<TravelReimbursementPage.ParticipantReimbursement>>(reimbursementPage.Model);
        var aliceReimbursement = reimbursements.First(r => r.EmailAddress.Equals("alice@example.org", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(65m, aliceReimbursement.Amount);
        var bobReimbursement = reimbursements.First(r => r.EmailAddress.Equals("bob@example.org", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(5m, bobReimbursement.Amount);

        // The second challenge setter picks Alice and Bob for an award,
        var companyJudgingPage = await system.ExecuteRequestAndAssertSuccessAsync(companyChallengeStorage, "/ChallengeSetter/Judging");
        var companyProjects = Assert.IsInstanceOfType<IReadOnlyCollection<Project>>(companyJudgingPage.Model);
        Assert.AreSequenceEqual(["FriendFinder"], companyProjects.Select(p => p.Title), StringComparer.Ordinal);
        await system.ExecuteRequestAndAssertSuccessAsync(
            companyChallengeStorage, "/ChallengeSetter/Judging/Edit",
            ("awards.Name", "1st place"),
            ("awards.ProjectId", companyProjects.Single().Id)
        );
        // and so does the first, who is not opt-in and thus can see the project anyway.
        var overallJudgingPage = await system.ExecuteRequestAndAssertSuccessAsync(overallChallengeStorage, "/ChallengeSetter/Judging");
        var overallProjects = Assert.IsInstanceOfType<IReadOnlyCollection<Project>>(overallJudgingPage.Model);
        Assert.AreSequenceEqual(["FriendFinder"], overallProjects.Select(p => p.Title), StringComparer.Ordinal);
        await system.ExecuteRequestAndAssertSuccessAsync(
            overallChallengeStorage, "/ChallengeSetter/Judging/Edit",
            ("awards.Name", "Top"),
            ("awards.ProjectId", overallProjects.Single().Id)
        );

        // The project is in the gallery,
        var gallery = await system.ExecuteRequestAsync<OperationResult.Projects>([], "/Projects");
        Assert.AreEqual("My Event", gallery.EventTitle, StringComparer.Ordinal);
        var project = Assert.ContainsSingle(gallery.Contents);
        Assert.AreEqual("FriendFinder", project.Title);
        Assert.AreSequenceEqual(["alice@example.org", "bob@example.org"], project.Team.Select(m => m.EmailAddress), SequenceOrder.InAnyOrder);
        // but as the event is not over its awards are not visible.
        Assert.IsEmpty(gallery.Awards);

        // The organizers can however view the awards in the challenges list.
        var adminChallengesPage = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Challenges");
        var challengesAndProjects = Assert.IsInstanceOfType<IReadOnlyCollection<ChallengesPage.ChallengeSetterAndProjects>>(adminChallengesPage.Model);
        Assert.HasCount(3, challengesAndProjects);
        Assert.AreEqual("Overall", challengesAndProjects.ElementAt(0).ChallengeSetter.Name, StringComparer.Ordinal);
        Assert.HasCount(1, challengesAndProjects.ElementAt(0).Projects);
        Assert.HasCount(1, challengesAndProjects.ElementAt(0).ChallengeSetter.Awards);
        Assert.AreEqual("Top", challengesAndProjects.ElementAt(0).ChallengeSetter.Awards.Single().Name);
        Assert.AreEqual(challengesAndProjects.ElementAt(0).Projects.Single().Key.Id, challengesAndProjects.ElementAt(0).ChallengeSetter.Awards.Single().ProjectId, StringComparer.Ordinal);
        Assert.AreEqual("Some Company", challengesAndProjects.ElementAt(1).ChallengeSetter.Name, StringComparer.Ordinal);
        Assert.HasCount(1, challengesAndProjects.ElementAt(1).Projects);
        Assert.HasCount(1, challengesAndProjects.ElementAt(1).ChallengeSetter.Awards);
        Assert.AreEqual("1st place", challengesAndProjects.ElementAt(1).ChallengeSetter.Awards.Single().Name);
        Assert.AreEqual(challengesAndProjects.ElementAt(1).Projects.Single().Key.Id, challengesAndProjects.ElementAt(0).ChallengeSetter.Awards.Single().ProjectId, StringComparer.Ordinal);
        Assert.AreEqual("Yet Another Company", challengesAndProjects.ElementAt(2).ChallengeSetter.Name, StringComparer.Ordinal);
        Assert.HasCount(1, challengesAndProjects.ElementAt(2).Projects);
        Assert.HasCount(0, challengesAndProjects.ElementAt(2).ChallengeSetter.Awards);

        // The event ends.
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/End/EndJudging");

        // The project now has awards in the gallery, in the right order.
        var endGallery = await system.ExecuteRequestAsync<OperationResult.Projects>([], "/Projects");
        var endProject = Assert.ContainsSingle(endGallery.Contents);
        Assert.AreEqual("FriendFinder", endProject.Title);
        Assert.HasCount(1, endGallery.Awards);
        Assert.AreSequenceEqual(["Overall Top", "Some Company 1st place"], endGallery.Awards[endProject], StringComparer.Ordinal);

        // The challenges list and projects gallery URL are case-insensitive.
        await system.ExecuteRequestAsync<OperationResult.Projects>([], "/pRoJeCts");
        await system.ExecuteRequestAsync<OperationResult.Challenges>([], "/cHaLlEnGeS");
    }

    [TestMethod]
    public async Task ParticipantCreatedAtCheckIn()
    {
        // The system is set up,
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);
        // applications close without anyone,
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Acceptance/Close");
        // and check in starts.
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/StartCheckIn/Start");

        // Admins check someone in by creating them.
        await system.ExecuteRequestAndAssertSuccessAsync(
            adminStorage, "/Admin/CheckIn/CheckInUnknown",
            ("emailAddress", "alice@example.org"),
            ("givenName", "Alice")
        );

        // That person can now log in,
        var participantStorage = new Dictionary<string, string>();
        var (loginEmail, _, loginAuthSecret) = system.DequeueEmail(assertSingle: true);
        Assert.IsNotNull(loginEmail.Operation);
        var emailOperation = Authenticator.AddAuthentication(loginAuthSecret, loginEmail.Operation, loginEmail.Recipient);
        var participantLoginResponse = await system.ExecuteRequestAsync<OperationResult.Page>(participantStorage, emailOperation.RelativeUri.ToString());
        Assert.AreEqual(Status.None, participantLoginResponse.Status);
        Assert.IsNotNull(participantLoginResponse.User);
        // and submit a project.
        await system.ExecuteRequestAndAssertSuccessAsync(
            participantStorage, "/Participant/Project/Edit",
            OperationArguments.FromPairs(("title", "FriendFinder"),
                                         ("shortDescription", "Find friends around you"),
                                         ("longDescription", "Make new friends who share the same interests"),
                                         ("link", "https://example.org/friend-finder"),
                                         ("challenges", "Sponsor One"))
                              .WithFile("thumbnail", new File.InMemory("name", "image/jpg", [0]))
        );
    }

    [TestMethod]
    public async Task ConcurrentReadOnlyRequestsAreAllowed()
    {
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);

        TaskCompletionSource[] tasks = [new(), new()];
        bool[] touched = [false, false];
        int index = 0;
        system.OperationHandler = () =>
        {
            touched[index] = true;
            var result = tasks[index].Task;
            index += 1;
            return result;
        };

        var request0 = system.ExecuteRequestAndAssertSuccessAsync([], "/");
        var request1 = system.ExecuteRequestAndAssertSuccessAsync([], "/");

        Assert.IsTrue(touched[0]);
        Assert.IsTrue(touched[1]);

        tasks[0].SetResult();
        tasks[1].SetResult();
        await request0;
        await request1;
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ConcurrentRequestsIncludingWriteAreSerialized(bool firstIsWrite, bool secondIsWrite)
    {
        var adminStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);

        TaskCompletionSource[] tasks = [new(), new()];
        bool[] touched = [false, false];
        int index = 0;
        system.OperationHandler = () =>
        {
            touched[index] = true;
            var result = tasks[index].Task;
            index += 1;
            return result;
        };

        var request0 =
            firstIsWrite ? system.ExecuteRequestAndAssertSuccessAsync(
                               [], "/Participant/Email/Edit",
                               ("emailAddress", "alice@example.org")
                           )
                         : system.ExecuteRequestAndAssertSuccessAsync([], "/");
        var request1 =
            secondIsWrite ? system.ExecuteRequestAndAssertSuccessAsync(
                                [], "/Participant/Email/Edit",
                                ("emailAddress", "bob@example.org")
                            )
                          : system.ExecuteRequestAndAssertSuccessAsync([], "/");

        Assert.IsTrue(touched[0]);
        Assert.IsFalse(touched[1]);

        tasks[0].SetResult();
        await request0;

        tasks[1].SetResult();
        await request1;
        Assert.IsTrue(touched[1]);
    }

    [TestMethod]
    public async Task ExportThenImportBackup()
    {
        var participantStorage = new Dictionary<string, string>();
        var adminStorage = new Dictionary<string, string>();

        // Setup
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, participantStorage, givenName: "Alice", emailAddress: "alice@example.org", stopAfterName: true);

        // Export, mimicking the fact the admin stored the backup file locally
        var export = await system.ExecuteRequestAsync<OperationResult.File>(adminStorage, "/Admin/Backup/Export");
        var downloadedBackup = new File.InMemory(export.RequestedFile.Name, export.RequestedFile.MimeType, await export.RequestedFile.ReadAsBytesAsync());

        // Some changes that shouldn't persist
        await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/EventLimits/Edit",
            ("limits.ApplicationGroupSize", "666"),
            ("limits.ProjectTeamSize", "999"),
            ("limits.DaysToConfirm", "333"),
            ("limits.DaysBetweenReminders", "111")
        );
        await system.ExecuteRequestAndAssertSuccessAsync(participantStorage, "/Participant/Name/Edit",
            ("givenName", "Diff"),
            ("familyName", "Erent")
        );
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, [], givenName: "Bob", emailAddress: "bob@example.org", confirm: true);

        // Import the backup, the changes should be canceled
        var result = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Backup/Import", OperationArguments.Empty.WithFile("backup", downloadedBackup));

        var participantsView = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Participants");
        var participants = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(participantsView.Model);
        var single = Assert.ContainsSingle(participants);
        Assert.AreEqual("Alice", single.GivenName);
        Assert.AreEqual("alice@example.org", single.EmailAddress);

        // Including in the summaries!
        var limitsView = result.AvailableViews.First(v => v.Title.Equals("Event limits", StringComparison.Ordinal));
        var limitItem = limitsView.Summary.First(s => s.Label.Equals("Application group size", StringComparison.Ordinal));
        Assert.AreNotEqual("666", limitItem.Text);
    }

    [TestMethod]
    [DataRow("admin@example.org")]
    [DataRow("new-admin@example.org")]
    public async Task ExportThenImportBackupOnDifferentSystem(string adminEmailAddressForImport)
    {
        // Setup system 1
        var participantStorage1 = new Dictionary<string, string>();
        var adminStorage1 = new Dictionary<string, string>();
        using var system1 = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system1, adminStorage1, openApplications: true);
        await CommonOperations.BasicParticipantApplicationAsync(system1, adminStorage1, participantStorage1, givenName: "Alice", emailAddress: "alice@example.org", stopAfterName: true);
        // Include a file in the user's profile, to ensure backups contain files
        var participantResume = new File.InMemory("Alice's Resume.pdf", "application/pdf", [0, 1, 2, 3]);
        await system1.ExecuteRequestAndAssertSuccessAsync(
            participantStorage1, "/Participant/Profile/Edit",
            OperationArguments.FromPairs(
                ("Choice One", "B"),
                ("Choice Two", "XYZ")
            ).WithFile("Resume", participantResume)
        );

        // Export the backup
        var export = await system1.ExecuteRequestAsync<OperationResult.File>(adminStorage1, "/Admin/Backup/Export");
        var downloadedBackup = new File.InMemory(export.RequestedFile.Name, export.RequestedFile.MimeType, await export.RequestedFile.ReadAsBytesAsync());

        // Create system 2, immediately import the backup without setup
        var adminStorage2 = new Dictionary<string, string>();
        using var system2 = new TestEventManagerSystem();
        await system2.ExecuteRequestAndAssertSuccessAsync(adminStorage2, "/Admin/EmailSetup/ImportBackup",
            OperationArguments.FromPairs(("adminEmailAddress", adminEmailAddressForImport))
                              .WithFile("backup", downloadedBackup)
        );

        // The admin should have received an email to confirm
        var (email, _, authSecret2) = system2.DequeueEmail();
        Assert.AreEqual(adminEmailAddressForImport, email.Recipient);
        Assert.IsNotNull(email.Operation);
        var emailOperation = Authenticator.AddAuthentication(authSecret2, email.Operation, email.Recipient);
        var adminLoginResponse = await system2.ExecuteRequestAndAssertSuccessAsync(adminStorage2, emailOperation.RelativeUri.ToString());
        Assert.IsNotNull(adminLoginResponse.User);

        if (adminEmailAddressForImport.Equals("admin@example.org", StringComparison.OrdinalIgnoreCase))
        {
            // At this point the admin storages should be equivalent, i.e., the auth secret should be the same
            Assert.AreEquivalent(adminStorage1, adminStorage2);
        }

        // There should be both admins
        var adminsView = await system2.ExecuteRequestAndAssertSuccessAsync(adminStorage2, "/Admin/Admins");
        var admins = Assert.IsInstanceOfType<IReadOnlyCollection<Admin>>(adminsView.Model);
        Assert.IsNotNull(admins.FirstOrDefault(a => a.EmailAddress.Equals("admin@example.org", StringComparison.OrdinalIgnoreCase)));
        Assert.IsNotNull(admins.FirstOrDefault(a => a.EmailAddress.Equals(adminEmailAddressForImport, StringComparison.OrdinalIgnoreCase)));
        var loggedInAdmin = Assert.IsInstanceOfType<Admin>(adminsView.User);
        Assert.AreEqual(adminEmailAddressForImport, loggedInAdmin.EmailAddress, StringComparer.OrdinalIgnoreCase);
        // the original participant,
        var participantsView = await system2.ExecuteRequestAndAssertSuccessAsync(adminStorage2, "/Admin/Participants");
        var participants = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(participantsView.Model);
        var single = Assert.ContainsSingle(participants);
        Assert.AreEqual("Alice", single.GivenName);
        Assert.AreEqual("alice@example.org", single.EmailAddress);
        // and their resume
        var resumeId = single.Profile["Resume"];
        var resumeFile = await system2.ExecuteRequestAsync<OperationResult.File>([], "/File/" + resumeId);
        Assert.AreEqual(participantResume.Name, resumeFile.RequestedFile.Name);
        Assert.AreEqual(participantResume.MimeType, resumeFile.RequestedFile.MimeType);
        Assert.AreSequenceEqual(participantResume.Contents, await resumeFile.RequestedFile.ReadAsBytesAsync());
    }

    [TestMethod]
    public async Task RequestWithEmptyFileFails()
    {
        var storage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, storage, openApplications: false);

        var error = await system.ExecuteRequestAsync<OperationResult.UserError>(
            storage, "/Admin/Backup/Import",
            OperationArguments.Empty.WithFile("backup", new File.InMemory("fake.backup", "application/octet-stream", []))
        );
        Assert.Contains("empty", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task RequestWithOversizedFileFromAdminDoesNotFail()
    {
        var storage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, storage, openApplications: false);

        var error = await system.ExecuteRequestAsync(
            storage, "/Admin/Backup/Import",
            OperationArguments.Empty.WithFile("backup", new File.InMemory("fake.backup", "application/octet-stream", new byte[File.MaxSizeInBytes + 1]))
        );
        if (error is OperationResult.UserError ue)
        {
            // not a valid backup, but should not fail for oversized reasons; code coverage will tell us if this doesn't cover what it's intended to
            Assert.DoesNotContain("oversized", ue.Message, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.IsInstanceOfType<OperationResult.SystemError>(error);
        }
    }

    [TestMethod]
    public async Task RequestWithOversizedFileFromParticipantFails()
    {
        var adminStorage = new Dictionary<string, string>();
        var participantStorage = new Dictionary<string, string>();
        using var system = new TestEventManagerSystem();
        await CommonOperations.BasicAdminConfigAsync(system, adminStorage, openApplications: true);
        await CommonOperations.BasicParticipantApplicationAsync(system, adminStorage, participantStorage, stopAfterName: true);

        var error = await system.ExecuteRequestAsync<OperationResult.UserError>(
             participantStorage, "/Participant/Profile/Edit",
             OperationArguments.FromPairs(
                 ("Choice One", "B"),
                 ("Choice Two", "XYZ")
             ).WithFile("Resume", new File.InMemory("fake.backup", "application/octet-stream", new byte[File.MaxSizeInBytes + 1]))
         );
        Assert.Contains("oversized", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

file static class CommonOperations
{
    public static async Task BasicAdminConfigAsync(TestEventManagerSystem system, Dictionary<string, string> storage, bool openApplications, uint projectTeamSize = 4)
    {
        var firstResponse = await system.ExecuteRequestAndAssertSuccessAsync(storage, "/");
        Assert.IsInstanceOfType<EmailSetupPage>(firstResponse.View.Page);

        await system.ExecuteRequestAndAssertSuccessAsync(
            storage, "/Admin/EmailSetup/Edit",
            ("adminEmailAddress", "admin@example.org"),
            ("settings.Uri", "https://mail.example.org/"),
            ("settings.UserName", "email-sender"),
            ("settings.Password", "email-password"),
            ("settings.SenderName", "email-sender-name"),
            ("settings.SenderAddress", "sender@example.org"),
            ("settings.ReplyToAddress", "replyto@example.org")
        );
        var (setupEmail, _, setupAuthSecret) = system.DequeueEmail(assertSingle: true);
        Assert.IsNotNull(setupEmail.Operation);

        var emailOperation = Authenticator.AddAuthentication(setupAuthSecret, setupEmail.Operation, setupEmail.Recipient);
        var adminLoginResponse = await system.ExecuteRequestAndAssertSuccessAsync(storage, emailOperation.RelativeUri.ToString());
        Assert.IsNotNull(adminLoginResponse.User);
        Assert.IsInstanceOfType<EventDetailsPage>(adminLoginResponse.View.Page);

        // At this point the email setup page must be private:
        await system.ExecuteRequestAsync<OperationResult.AuthenticationRequired>([], "/Admin/EmailSetup");
        // The gallery is not available now:
        await system.ExecuteRequestAsync<OperationResult.Unavailable>([], "/Projects");
        // Neither is the challenges list:
        await system.ExecuteRequestAsync<OperationResult.Unavailable>([], "/Challenges");

        // Nothing bad should happen if the periodic tasks are triggered:
        await system.RunPeriodicTasksAsync();

        await system.ExecuteRequestAndAssertSuccessAsync(
            storage, "/Admin/EventDetails/Edit",
            ("details.Title", "My Event"),
            ("details.Location", "Who knows?"),
            ("details.TimeZone", "Europe/Zurich"),
            ("details.Start", "3000-06-01T08:30"),
            ("details.End", "3000-06-02T18:30"),
            ("details.ConfirmationText", "The event will be great. Maybe."),
            ("details.WebsiteUrl", "https://example.org/main"),
            ("details.HelpUrl", "https://example.org/help"),
            ("details.PrivacyPolicy", "Event organizers have unlimited and irrevocable ownership of your soul."),
            ("details.Challenges", "Sponsor One"),
            ("details.Challenges", "Sponsor Two")
        );

        await system.ExecuteRequestAndAssertSuccessAsync(
            storage, "/Admin/EventLimits/Edit",
            ("limits.ApplicationGroupSize", "4"),
            ("limits.ProjectTeamSize", projectTeamSize.ToString(CultureInfo.InvariantCulture)),
            ("limits.DaysToConfirm", "10"),
            ("limits.DaysBetweenReminders", "3")
        );

        await system.ExecuteRequestAndAssertSuccessAsync(
            storage, "/Admin/EventTheme/Edit",
            OperationArguments.FromPairs(("backgroundColor", "#f00000"))
                              .WithFile("logo", new File.InMemory("name", "image/png", [0]))
                              .WithFile("icon", new File.InMemory("name", "image/png", [0]))
        );

        await system.ExecuteRequestAndAssertSuccessAsync(
            storage, "/Admin/ProfileForm/Edit",
            OperationArguments.FromPairs(
                ("form.Choices.Name", "Choice One"),
                ("form.Choices.Description", "Pick something"),
                ("form.Choices.IsRequired", "true"),
                ("form.Choices.Options", "A\nB\nC"),
                ("form.Choices.AllowsCustomOption", "false"),
                ("form.Choices.CustomOptionSuggestions", ""),
                ("form.Choices.Name", "Choice Two"),
                ("form.Choices.Description", "Pick or input"),
                ("form.Choices.IsRequired", "true"),
                ("form.Choices.Options", "Existing"),
                ("form.Choices.AllowsCustomOption", "true"),
                ("form.Choices.CustomOptionSuggestions", "Suggestion\nOther suggestion"),
                ("form.Files.Name", "Resume"),
                ("form.Files.Description", "Your resume"),
                ("form.Files.IsRequired", "false"),
                ("form.Files.AllowedExtensions", ".pdf")
            )
        );

        if (openApplications)
        {
            await system.ExecuteRequestAndAssertSuccessAsync(storage, "/Admin/OpenApplications/Open");
        }

        // Check that the first action was logged
        var auditPage = await system.ExecuteRequestAndAssertSuccessAsync(storage, "/Admin/Audit");
        var logs = Assert.IsInstanceOfType<IReadOnlyCollection<AuditMessage>>(auditPage.Model);
        var log = logs.First();
        Assert.AreEqual(Status.ImportantInformation, log.Status);
        Assert.IsNull(log.EmailAddress);
        Assert.AreEqual("/Admin/EmailSetup/Edit", log.Source);
        // Nothing should have caused a system error:
        // (this is a regression test)
        Assert.IsEmpty(logs.Where(l => l.Status == Status.SystemError));
    }

    public static async Task BasicParticipantApplicationAsync(TestEventManagerSystem system, Dictionary<string, string> adminStorage, Dictionary<string, string> participantStorage,
                                                              string givenName = "Alice", string emailAddress = "alice@example.org", bool stopAfterName = false, bool confirm = true)
    {
        await system.ExecuteRequestAndAssertSuccessAsync(
            participantStorage, "/Participant/Email/Edit",
            ("emailAddress", emailAddress),
            ("referrer", "LonkedOn")
        );
        var (loginEmail, _, loginAuthSecret) = system.DequeueEmail(assertSingle: true);
        Assert.IsNotNull(loginEmail.Operation);

        var emailOperation = Authenticator.AddAuthentication(loginAuthSecret, loginEmail.Operation, loginEmail.Recipient);
        var participantLoginResponse = await system.ExecuteRequestAsync<OperationResult.Page>(participantStorage, emailOperation.RelativeUri.ToString());
        Assert.AreEqual(Status.Success, participantLoginResponse.Status);
        Assert.IsNotNull(participantLoginResponse.User);

        // At this point the participant can't do something illegal like try to confirm
        await system.ExecuteRequestAsync<OperationResult.Unavailable>(participantStorage, "/Participant/WaitForAcceptance/Confirm");

        // The participant has no family name but forgets to set the family name field to the required placeholder, which is an error
        await system.ExecuteRequestAsync<OperationResult.UserError>(
            participantStorage, "/Participant/Name/Edit",
            ("givenName", givenName)
        );

        await system.ExecuteRequestAndAssertSuccessAsync(
            participantStorage, "/Participant/Name/Edit",
            ("givenName", givenName),
            ("familyName", NamePage.EmptyFamilyNamePlaceholder)
        );

        // Sanity check that the participant is what we expect
        var participantsPage = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Participants");
        var participants = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(participantsPage.Model);
        var participant = Assert.ContainsSingle(participants.Where(p => p.EmailAddress.Equals(emailAddress, StringComparison.Ordinal)));
        Assert.AreEqual(givenName, participant.GivenName);
        Assert.IsNull(participant.FamilyName);
        Assert.AreEqual("LonkedOn", participant.Referrer);

        if (!stopAfterName)
        {
            await system.ExecuteRequestAndAssertSuccessAsync(
                participantStorage, "/Participant/Profile/Edit",
                ("Choice One", "B"),
                ("Choice Two", "XYZ")
            );

            await system.ExecuteRequestAndAssertSuccessAsync(participantStorage, "/Participant/Group/Finalize");
            var (finalizeEmail, _, _) = system.DequeueEmail(assertSingle: true);
            Assert.AreEqual(emailAddress, finalizeEmail.Recipient);

            // At this point admins can accept the participant
            await system.ExecuteRequestAndAssertSuccessAsync(
                adminStorage, "/Admin/Acceptance/Accept",
                ("count", "2"),
                ("random", "true")
            );

            // Once accepted, the participant must interact with the email to confirm, simply querying the system isn't enough
            var page = await system.ExecuteRequestAsync<OperationResult.Page>(participantStorage, "/Participant");
            Assert.IsInstanceOfType<WaitForAcceptancePage>(page.View.Page);

            // The participant can now confirm
            var (confirmEmail, _, _) = system.DequeueEmail(assertSingle: true);

            if (confirm)
            {
                Assert.IsNotNull(confirmEmail.Operation);
                await system.ExecuteRequestAndAssertSuccessAsync(participantStorage, confirmEmail.Operation.RelativeUri.ToString(), confirmEmail.Operation.Arguments);
                // And receives a final confirmation email
                system.DequeueEmail(assertSingle: true);

                var lastParticipantPage = await system.ExecuteRequestAsync<OperationResult.Page>(participantStorage, "/");
                Assert.IsInstanceOfType<TravelPage>(lastParticipantPage.View.Page);

                var participantsPage3 = await system.ExecuteRequestAndAssertSuccessAsync(adminStorage, "/Admin/Participants");
                var participants3 = Assert.IsInstanceOfType<IReadOnlyCollection<Participant>>(participantsPage3.Model);
                var participant3 = Assert.ContainsSingle(participants3.Where(p => p.EmailAddress.Equals(emailAddress, StringComparison.OrdinalIgnoreCase)));
                Assert.AreEqual(givenName, participant3.GivenName);
                Assert.IsNull(participant3.FamilyName);
                Assert.AreEqual("LonkedOn", participant3.Referrer);
            }
        }
    }
}

file sealed class TestRequest(Dictionary<string, string> storage, string? relativeUri, OperationArguments extraArgs)
{
    private const string Host = "https://example.com";

    public Dictionary<string, string> Storage { get; } = storage;
    public Uri? Uri { get; } = relativeUri is null ? null : new(Host + relativeUri);
    public OperationArguments OperationArguments { get; } = extraArgs;
}

file sealed class TestIgnoredException : Exception;

file sealed class TestEventManagerSystem : EventManagerSystem<TestRequest>, IDisposable
{
    private readonly string _dbFilePath = System.IO.Path.GetTempFileName();
    private readonly string _storageRootPath = System.IO.Directory.CreateTempSubdirectory("FileStorage").FullName;

    private readonly Queue<(Email, EmailSenderSettings, AuthenticationSecret)> _outbox = [];
    private readonly Dictionary<TestRequest, OperationResult> _currentResults = [];

    public FakeTimeProvider TimeProvider { get; } = new(new DateTimeOffset(2025, 11, 22, 9, 0, 0, 0, TimeSpan.FromHours(1)));

    // if set, HandleOperationResult uses this instead of returning a completed task
    public Func<Task>? OperationHandler { get; set; }

    public bool CrashWhenGettingUri { get; set; }
    public bool CrashWhenCreatingDatabase { get; set; }
    public bool CrashWhenCreatingEmailSender { get; set; }
    public bool CrashWhenCreatingEmailSenderWithIgnoredException { get; set; }
    public bool CrashWhenCreatingEmailSenderWithOperationCanceledException { get; set; }
    public bool CrashWhenCreatingTimeProvider { get; set; }

    public bool UseCrashyDatabase { get; set; }

    protected override bool ShouldLogException(Exception e)
        => base.ShouldLogException(e) && e is not TestIgnoredException;

    protected override Db CreateDb()
    {
        if (CrashWhenCreatingDatabase)
        {
            throw new InvalidOperationException("Fake crash");
        }
        if (UseCrashyDatabase)
        {
            return new CrashyDatabase(new EntityFrameworkDb(_dbFilePath));
        }
        return new EntityFrameworkDb(_dbFilePath);
    }

    protected override FileStorage CreateFileStorage()
        => new DiskFileStorage(_storageRootPath);

    protected override EmailSender CreateEmailSender(Uri baseUri, Config config)
    {
        if (CrashWhenCreatingEmailSender)
        {
            throw new InvalidOperationException("Fake crash");
        }
        if (CrashWhenCreatingEmailSenderWithIgnoredException)
        {
            throw new TestIgnoredException();
        }
        if (CrashWhenCreatingEmailSenderWithOperationCanceledException)
        {
            throw new OperationCanceledException();
        }
        return new TestEmailSender(_outbox, config.EmailSenderSettings, config.AuthenticationSecret);
    }

    protected override TimeProvider CreateTimeProvider()
    {
        if (CrashWhenCreatingTimeProvider)
        {
            throw new InvalidOperationException("Fake crash");
        }
        return TimeProvider;
    }

    protected override ClientSideStorage GetClientSideStorage(TestRequest request)
        => new TestClientSideStorage(request);

    protected override Uri? GetUri(TestRequest request)
    {
        if (CrashWhenGettingUri)
        {
            throw new InvalidOperationException("Fake crash");
        }
        return request.Uri;
    }

    protected override Task HandleOperationResultAsync(OperationResult result, TestRequest request)
    {
        // technically ok but should never happen
        Assert.IsFalse(_currentResults.ContainsKey(request), "HandleOperationResult called more than once for a request");
        _currentResults[request] = result;

        if (OperationHandler is not null)
        {
            return OperationHandler();
        }

        return Task.CompletedTask;
    }

    protected override Task<OperationArguments> ParseExtraArgumentsAsync(TestRequest request)
        => Task.FromResult(request.OperationArguments);

    public Task<OperationResult.Page> ExecuteRequestAndAssertSuccessAsync(Dictionary<string, string> storage, string? relativeUri, params (string, string)[] args)
        => ExecuteRequestAndAssertSuccessAsync(storage, relativeUri, OperationArguments.FromPairs(args));

    public async Task<OperationResult.Page> ExecuteRequestAndAssertSuccessAsync(Dictionary<string, string> storage, string? relativeUri, OperationArguments args)
    {
        var response = await ExecuteRequestAsync<OperationResult.Page>(storage, relativeUri, args);
        if (response.Status is not (Status.Success or Status.ImportantInformation or Status.None))
        {
            throw new AssertFailedException("Unexpected status: " + response.Status + " / " + response.Message);
        }
        return response;
    }

    public Task<TResult> ExecuteRequestAsync<TResult>(Dictionary<string, string> storage, string? relativeUri, params (string, string)[] args) where TResult : OperationResult
        => ExecuteRequestAsync<TResult>(storage, relativeUri, OperationArguments.FromPairs(args));

    public async Task<TResult> ExecuteRequestAsync<TResult>(Dictionary<string, string> storage, string? relativeUri, OperationArguments? args = null) where TResult : OperationResult
    {
        var result = await ExecuteRequestAsync(storage, relativeUri, args);
        return Assert.IsInstanceOfType<TResult>(result, $"Expected a page but got: {result} of type {result.GetType()}");
    }

    public async Task<OperationResult> ExecuteRequestAsync(Dictionary<string, string> storage, string? relativeUri, OperationArguments? args = null)
    {
        var request = new TestRequest(storage, relativeUri, args ?? OperationArguments.Empty);
        await RunAsync(request);
        Assert.IsTrue(_currentResults.Remove(request, out var result), "HandleOperationResult not called for a request");
        return result;
    }

    public (Email, EmailSenderSettings, AuthenticationSecret) DequeueEmail(bool assertSingle = true)
    {
        var result = _outbox.Dequeue();
        if (assertSingle)
        {
            Assert.IsEmpty(_outbox);
        }
        return result;
    }

    public async Task<string> GetChallengeSetterLoginRelativeUriAsync(string name)
    {
        var db = CreateDb();
        var config = await Config.CreateAsync(db);
        return Authenticator.AddAuthentication(config.AuthenticationSecret!, Operation.CreatePageView<ChallengeSetter>(), name).RelativeUri.ToString();
    }

    public void Dispose()
    {
        System.IO.File.Delete(_dbFilePath);
        System.IO.Directory.Delete(_storageRootPath, true);
    }
}

file sealed class TestClientSideStorage(TestRequest request) : ClientSideStorage
{
    public override void Set(string key, string value)
        => request.Storage[key] = value;

    public override bool TryGet(string key, [MaybeNullWhen(false)] out string value)
        => request.Storage.TryGetValue(key, out value);
}

file sealed class TestEmailSender(Queue<(Email, EmailSenderSettings, AuthenticationSecret)> outbox, EmailSenderSettings? settings, AuthenticationSecret? authSecret) : EmailSender
{
    public override async Task SendAsync(IReadOnlyCollection<Email> emails, EmailSenderSettings? overrideSettings = null, AuthenticationSecret? overrideSecret = null)
    {
        foreach (var email in emails)
        {
            var settingsInUse = overrideSettings ?? settings;
            Assert.IsNotNull(settingsInUse);
            var secretInUse = overrideSecret ?? authSecret;
            Assert.IsNotNull(secretInUse);
            outbox.Enqueue((email, settingsInUse, secretInUse));
        }
    }

    public override async Task SendCopyAsync(string subject, string body, IReadOnlyCollection<string> recipients, Operation? operation = null, string? operationDescription = null)
    {
        if (settings is null || authSecret is null)
        {
            throw new InvalidOperationException($"Cannot use {nameof(SendCopyAsync)} without settings & auth secret");
        }

        foreach (var recipient in recipients)
        {
            outbox.Enqueue((new Email(recipient, subject, body, operation, operationDescription), settings, authSecret));
        }
    }
}

file sealed class CrashyDatabase(Db wrapped) : Db
{
    public override DbValues<Admin> Admins => wrapped.Admins;
    public override DbValues<ApplicationGroup> ApplicationGroups => wrapped.ApplicationGroups;
    public override DbValues<AuditMessage> AuditMessages => wrapped.AuditMessages;
    public override DbValues<Award> Awards => wrapped.Awards;
    public override DbValues<ChallengeSetter> ChallengeSetters => wrapped.ChallengeSetters;
    public override DbValues<StoredConfigValue> ConfigValues => wrapped.ConfigValues;
    public override DbValues<Currency> Currencies => wrapped.Currencies;
    public override DbValues<Letter> Letters => wrapped.Letters;
    public override DbValues<Participant> Participants => wrapped.Participants;
    public override DbValues<Project> Projects => wrapped.Projects;
    public override DbValues<TravelExpense> TravelExpenses => wrapped.TravelExpenses;

    public override Task InitializeAsync() => wrapped.InitializeAsync();
    public override void EnsureNoChanges() => wrapped.EnsureNoChanges();
    public override void CancelChanges() => wrapped.CancelChanges();
    public override Task<System.IO.Stream> ExportAndDisposeAsync() => wrapped.ExportAndDisposeAsync();
    public override Task OverwriteAsync(System.IO.Stream stream) => wrapped.OverwriteAsync(stream);
    public override ValueTask DisposeAsync() => wrapped.DisposeAsync();

    public override Task<bool> CommitAsync()
    {
        throw new NotSupportedException("Crash!");
    }
}