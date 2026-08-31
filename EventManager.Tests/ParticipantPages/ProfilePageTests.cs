using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.ParticipantPages;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

[TestClass]
public sealed class ProfilePageTests : ParticipantTestsBase
{
    [TestMethod]
    public async Task PageIsHiddenWhenFormIsEmpty()
    {
        var form = new ProfileForm([], []);
        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsEmpty(view.Summary);
        Assert.IsNull(view.Action);
    }

    [TestMethod]
    public async Task PageIsRequiredOnFirstTimeWhenSomeChoicesAreRequired()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", true, [], true, []),
            new ProfileFormChoice("Drink", "Drink?", false, [], true, [])
        ], []);
        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsRequiredOnFirstTimeWhenAllChoicesAreOptional()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", false, [], true, [])
        ], []);
        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    public async Task PageIsRequiredWhenParticipantDidNotFillRequiredChoices()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", true, [], true, []),
            new ProfileFormChoice("Drink", "Drink?", false, [], true, [])
        ], []);

        {
            await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.FromPairs(("Drink", "Apple juice")));
            await Db.CommitAsync();
        }

        {
            var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());
            Assert.IsTrue(view.IsRequired);
            Assert.IsTrue(view.IsInteractable);
        }

        {
            var newParticipant = await GetParticipantAsync();
            Assert.IsNotNull(newParticipant);
            Assert.AreNotEqual(ParticipantStatus.ProfileFilled, newParticipant.Status);
        }
    }

    [TestMethod]
    public async Task PageIsEditableWhenParticipantFilledRequiredChoices()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", true, [], true, []),
            new ProfileFormChoice("Drink", "Drink?", false, [], true, [])
        ], []);

        {
            await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.FromPairs(("Food", "Apple")));
            await Db.CommitAsync();
        }

        {
            var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());
            Assert.IsFalse(view.IsRequired);
            Assert.IsTrue(view.IsInteractable);
        }

        {
            var newParticipant = await GetParticipantAsync();
            Assert.IsNotNull(newParticipant);
            Assert.AreEqual(ParticipantStatus.ProfileFilled, newParticipant.Status);
        }
    }

    [TestMethod]
    [DataRow(ParticipantStatus.Finalized)]
    [DataRow(ParticipantStatus.Accepted)]
    public async Task PageIsSummaryOnlyWhenParticipantIsFinalizedOrAccepted(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            participant.Profile = ImmutableDictionary<string, string>.Empty.Add("Choice", "123");
            await Db.CommitAsync();
        }

        var form = new ProfileForm([new("Choice", "descr", false, [], true, [])], []);
        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsNotEmpty(view.Summary);
    }

    [TestMethod]
    public async Task PageIsForbiddenWithEmptyProfileForm()
    {
        var form = new ProfileForm([], []);
        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.IsFalse(view.IsRequired);
        Assert.IsFalse(view.IsInteractable);
        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    public async Task SubmitReturnsErrorWhenRequiredChoiceIsNotProvided()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", true, [], true, []),
        ], []);

        {
            var result = await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.FromPairs(("Drink", "Syrup")));
            Assert.AreEqual(Status.UserError, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await Db.Participants.FindAsync(ParticipantEmailAddress);
        Assert.IsNotNull(newParticipant);
        Assert.IsFalse(newParticipant.Profile.ContainsKey("Drink"));
    }

    [TestMethod]
    public async Task SubmitReturnsErrorWhenRequiredChoiceIsTooLong()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", true, [], true, []),
        ], []);

        {
            var result = await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.FromPairs(("Food", new string('x', ProfileFormChoice.MaxCustomOptionLength + 1))));
            Assert.AreEqual(Status.UserError, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await Db.Participants.FindAsync(ParticipantEmailAddress);
        Assert.IsNotNull(newParticipant);
        Assert.IsFalse(newParticipant.Profile.ContainsKey("Drink"));
    }

    [TestMethod]
    public async Task SubmitReturnsErrorWhenRequiredFileIsNotProvided()
    {
        var form = new ProfileForm([],
        [
            new ProfileFormFile("CV", "Resume", true, [".pdf"]),
        ]);
        var result = await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.FromPairs(("Drink", "Syrup")));

        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task SubmitSavesProvidedDataWhenRequiredItemIsNotProvided()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", true, [], true, []),
            new ProfileFormChoice("Drink", "Drink?", false, [], true, []),
        ],
        [
            new ProfileFormFile("CV", "Resume", true, [".pdf"]),
        ]);
        var file = new File.InMemory("name", "text/plain", [0, 1, 2, 3]);

        {
            var result = await CreatePage(form).EditAsync(await GetParticipantAsync(),
                OperationArguments.FromPairs(("Drink", "Orange juice"))
                                  .WithFile("CV", file)
            );
            Assert.AreEqual(Status.UserError, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await Db.Participants.FindAsync(ParticipantEmailAddress);
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual("Orange juice", newParticipant.Profile["Drink"]);
        var storedFile = await FileStorage.GetFileAsync(newParticipant.Profile["CV"]);
        Assert.IsNotNull(storedFile);
        Assert.AreEqual(file.MimeType, storedFile.MimeType);
    }

    [TestMethod]
    public async Task SubmitSavesProvidedDataAndReturnsSuccessWhenParticipantProvidesAllRequiredItems()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", true, [], true, []),
            new ProfileFormChoice("Drink", "Drink?", false, [], true, []),
            new ProfileFormChoice("Unicorn", "Unicorn?", false, ["yes"], false, []),
        ],
        [
            new ProfileFormFile("CV", "Resume", true, [".pdf"]),
        ]);
        var file = new File.InMemory("name", "text/plain", [0, 1, 2, 3]);

        {
            var result = await CreatePage(form).EditAsync(await GetParticipantAsync(),
                OperationArguments.FromPairs(("Food", "\tApple    "), ("Unicorn", "  yes\n")) // test that we're trimming properly
                                  .WithFile("CV", file)
            );
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await Db.Participants.FindAsync(ParticipantEmailAddress);
        Assert.IsNotNull(newParticipant);
        Assert.AreEqual("Apple", newParticipant.Profile["Food"]);
        Assert.AreEqual("yes", newParticipant.Profile["Unicorn"]);
        Assert.IsFalse(newParticipant.Profile.ContainsKey("Drink"));
        var storedFile = await FileStorage.GetFileAsync(newParticipant.Profile["CV"]);
        Assert.IsNotNull(storedFile);
        Assert.AreEqual(file.MimeType, storedFile.MimeType);
    }

    [TestMethod]
    public async Task SubmitCanRemoveOptionalSingleChoice()
    {
        var form = new ProfileForm(
            [new ProfileFormChoice("choice", "descr", false, ["single"], false, [])], []
        );

        {
            var result = await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.FromPairs(("choice", "single")));
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        {
            var result2 = await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.Empty);
            Assert.AreEqual(Status.Success, result2.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await Db.Participants.FindAsync(ParticipantEmailAddress);
        Assert.IsNotNull(newParticipant);
        Assert.IsEmpty(newParticipant.Profile);
    }

    [TestMethod]
    public async Task SubmitCanAddOptionalAndPreviouslyNotGivenFile()
    {
        var form = new ProfileForm([],
        [
            new ProfileFormFile("CV", "Resume", false, [".pdf"]),
        ]);

        {
            var result = await CreatePage(form).EditAsync(await GetParticipantAsync(), FileFormValues());
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        {
            var result2 = await CreatePage(form).EditAsync(await GetParticipantAsync(), FileFormValues(("CV", "text/plain")));
            Assert.AreEqual(Status.Success, result2.Status);
            await Db.CommitAsync();
        }

        var newParticipant = await Db.Participants.FindAsync(ParticipantEmailAddress);
        Assert.IsNotNull(newParticipant);
        var storedFile = await FileStorage.GetFileAsync(newParticipant.Profile["CV"]);
        Assert.IsNotNull(storedFile);
        Assert.AreEqual("text/plain", storedFile.MimeType);
    }

    [TestMethod]
    public async Task SubmitCanRemoveFile()
    {
        var form = new ProfileForm([],
        [
            new ProfileFormFile("CV", "Resume", false, [".pdf"]),
        ]);

        {
            var result = await CreatePage(form).EditAsync(await GetParticipantAsync(), FileFormValues(("CV", "text/plain")));
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        string fileId;
        {
            var newParticipant = await Db.Participants.FindAsync(ParticipantEmailAddress);
            Assert.IsNotNull(newParticipant);
            fileId = newParticipant.Profile["CV"];
            Assert.IsNotNull(await FileStorage.GetFileAsync(fileId));
        }

        {
            var result2 = await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.FromPairs((ProfilePage.FileRemovalPrefix + "CV", "true")));
            await Db.CommitAsync();
            Assert.AreEqual(Status.Success, result2.Status);
        }

        var newParticipant2 = await Db.Participants.FindAsync(ParticipantEmailAddress);
        Assert.IsNotNull(newParticipant2);
        Assert.IsFalse(newParticipant2.Profile.ContainsKey("CV"));
        Assert.IsNull(await FileStorage.GetFileAsync(fileId));
    }

    [TestMethod]
    public async Task SubmitCanReplaceFile()
    {
        var form = new ProfileForm([],
        [
            new ProfileFormFile("CV", "Resume", false, [".pdf"]),
        ]);

        {
            var result = await CreatePage(form).EditAsync(await GetParticipantAsync(), FileFormValues(("CV", "text/plain")));
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        string fileId;
        {
            var newParticipant = await Db.Participants.FindAsync(ParticipantEmailAddress);
            Assert.IsNotNull(newParticipant);
            fileId = newParticipant.Profile["CV"];
            Assert.IsNotNull(await FileStorage.GetFileAsync(fileId));
        }

        {
            var result2 = await CreatePage(form).EditAsync(await GetParticipantAsync(),
                OperationArguments.FromPairs((ProfilePage.FileRemovalPrefix + "CV", "true"))
                                  .WithFile("CV", new File.InMemory("name", "text/someotherkind", [0, 1]))
            );
            Assert.AreEqual(Status.Success, result2.Status);
            await Db.CommitAsync();
        }

        var newParticipant2 = await Db.Participants.FindAsync(ParticipantEmailAddress);
        Assert.IsNotNull(newParticipant2);
        var newFile = await FileStorage.GetFileAsync(newParticipant2.Profile["CV"]);
        Assert.IsNotNull(newFile);
        Assert.AreEqual("text/someotherkind", newFile.MimeType);
    }

    [TestMethod]
    public async Task SummaryContainsSelectedFormChoices()
    {
        var form = new ProfileForm(
        [
            new ProfileFormChoice("Food", "Food?", true, [], true, []),
            new ProfileFormChoice("Drink", "Drink?", false, [], true, []),
            new ProfileFormChoice("Required", "Required", true, ["Accept"], false, []) // required and single choice so should not be shown
        ], []);

        {
            await CreatePage(form).EditAsync(await GetParticipantAsync(), OperationArguments.FromPairs(("Food", "Apple"), ("Required", "Accept")));
            await Db.CommitAsync();
        }

        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.AreSequenceEqual([("Food", "Apple")], view.Summary);
    }

    [TestMethod]
    public async Task SummaryContainsProvidedFormFiles()
    {
        var form = new ProfileForm([],
        [
            new ProfileFormFile("CV", "Resume", true, [".pdf"]),
            new ProfileFormFile("Essay", "500 words max", false, [".docx"])
        ]);

        {
            await CreatePage(form).EditAsync(await GetParticipantAsync(), FileFormValues(("CV", "text/plain")));
            await Db.CommitAsync();
        }

        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.AreSequenceEqual([("CV", "provided")], view.Summary);
    }

    [TestMethod]
    public async Task SummaryIsEmptyForForcefullyCheckedInParticipant()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.CheckedIn;
            await Db.CommitAsync();
        }

        var form = new ProfileForm([],
        [
            new ProfileFormFile("CV", "Resume", true, [".pdf"]),
            new ProfileFormFile("Essay", "500 words max", false, [".docx"])
        ]);
        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    [DataRow(ParticipantStatus.ProfileFilled)]
    [DataRow(ParticipantStatus.Finalized)]
    public async Task SummaryIsEmptyIfFormHasOnlyNonFreeChoices(ParticipantStatus status)
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = status;
            participant.Profile = participant.Profile.Add("ok", "yes");
            await Db.CommitAsync();
        }

        var form = new ProfileForm(
            [
                new ProfileFormChoice("ok", "please", true, ["yes"], false, [])
            ], []
        );
        var view = await CreatePage(form).ViewAsync(await GetParticipantAsync());

        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    public async Task WithdrawWithdrawsAndSendsEmail()
    {
        var page = CreatePage(new ProfileForm([], []), disableEmails: false);
        var result = await page.WithdrawAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var email = Assert.ContainsSingle(EmailSender.Outbox);
        Assert.AreEqual(ParticipantEmailAddress, email.Recipient, StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task WithdrawDoesNothingIfAlreadyWithdrawn()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.WithdrawnBeforeConfirmation;
            await Db.CommitAsync();
        }

        var page = CreatePage(new ProfileForm([], []));
        var result = await page.WithdrawAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.None, result.Status);
    }

    [TestMethod]
    public async Task WithdrawFailsWhenProfileAlreadyFilled()
    {
        {
            var participant = await GetParticipantAsync();
            participant.Status = ParticipantStatus.ProfileFilled;
            await Db.CommitAsync();
        }

        var page = CreatePage(new ProfileForm([], []));
        var result = await page.WithdrawAsync(await GetParticipantAsync());
        Assert.AreEqual(Status.UserError, result.Status);
    }

    private ProfilePage CreatePage(ProfileForm form, bool disableEmails = true)
        => new(form, FileStorage, disableEmails ? DisabledEmailSender : EmailSender);

    private static OperationArguments FileFormValues(params (string, string)[] values)
        => values.Aggregate(OperationArguments.Empty, (o, p) => o.WithFile(p.Item1, new File.InMemory(p.Item1, p.Item2, [0, 1, 2, 3])));
}