using System;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class BackupPageTests : AdminTestsBase
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PageIsOptionalForOwnersAndHiddenFromOthers(bool isOwner)
    {
        var page = new BackupPage(Db, EventDetails, new([], []), FileStorage, DisabledTimeProvider);

        var admin = isOwner ? await GetAdminAsync()
                            : await CreateNonOwnerAdminAsync();
        var view = await page.ViewAsync(admin);

        Assert.IsFalse(view.IsRequired);
        Assert.AreEqual(isOwner, view.IsInteractable);
        Assert.IsEmpty(view.Summary);
    }

    [TestMethod]
    public async Task ExportParticipantsCsvHasExpectedContent()
    {
        var form = new ProfileForm([
            new ProfileFormChoice("Starter", "Your starter Pokémon", true, [], true, []),
            new ProfileFormChoice("Confirm", "Tick the box", true, ["I confirm"], false, []) // should not be in the CSV
        ], []);
        {
            var alice = new Participant("alice@example.org")
            {
                GivenName = "Alice",
                FamilyName = "Apple\r\nOrange",
                Profile = ImmutableDictionary<string, string>.Empty.Add("Starter", "Cyndaquil"),
                AdminRemarks = "Some \"admin remarks\"",
                Referrer = "LonkedOn",
                Status = ParticipantStatus.Confirmed
            };
            var bob = new Participant("bob@example.org")
            {
                GivenName = "Bob, Jr",
                FamilyName = "Banana\nGrapefruit",
                Profile = ImmutableDictionary<string, string>.Empty.Add("Starter", "Chikorita"),
                IsSoftRejected = true,
                Status = ParticipantStatus.Finalized
            };
            var carol = new Participant("carol@example.org")
            {
                GivenName = "Çářôℓ",
                FamilyName = "Çôçôñúƭ",
                Profile = ImmutableDictionary<string, string>.Empty.Add("Starter", "Torchic"),
                Status = ParticipantStatus.Accepted
            };
            var daniel = new Participant("daniel@example.org")
            {
                Status = ParticipantStatus.EmailAddressVerified
            };
            var eve = new Participant("eve@example.org")
            {
                Status = ParticipantStatus.Created // should not be in the CSV
            };
            var franz = new Participant("franz@example.org")
            {
                GivenName = "Franz",
                FamilyName = "Ferdinand",
                Profile = ImmutableDictionary<string, string>.Empty.Add("Starter", "Bulbasaur"),
                Status = ParticipantStatus.WithdrawnBeforeConfirmation
            };
            Db.Participants.Add(alice, bob, carol, daniel, eve, franz);
            Db.ApplicationGroups.Add(new("ac") { Members = { alice, carol } }, new("b") { Members = { bob } }, new("f") { Members = { franz } });
            await Db.CommitAsync();
        }

        var page = new BackupPage(Db, EventDetails, form, FileStorage, DisabledTimeProvider);
        var csv = await page.ExportParticipantsCsvAsync();
        var csvBytes = await csv.ReadAsBytesAsync();

        // The checks below have been manually confirmed to lead to a file Microsoft Excel can properly parse.
        // It's messy because we want to make sure the exact kind of newline (CRLF vs LF) is preserved so we can't easily use C#'s multiline string literals.
        Assert.AreSequenceEqual<byte>([0xEF, 0xBB, 0xBF], csvBytes[..3]);
        string expected = "\"#\",\"Email address\",\"Given name\",\"Family name\",\"Status\",\"Soft rejected?\",\"Starter\",\"Admin remarks\",\"Referrer\",\"Application Group #\"\r\n\"1\",\"alice@example.org\",\"Alice\",\"Apple\r\nOrange\",\"Confirmed, not checked in yet\",\"\",\"Cyndaquil\",\"Some \"\"admin remarks\"\"\",\"LonkedOn\",\"G0\"\r\n\"2\",\"bob@example.org\",\"Bob, Jr\",\"Banana\nGrapefruit\",\"Finalized\",\"true\",\"Chikorita\",,,\"G1\"\r\n\"3\",\"franz@example.org\",\"Franz\",\"Ferdinand\",\"Withdrawn before confirmation\",\"\",\"Bulbasaur\",,,\"G2\"\r\n\"4\",\"carol@example.org\",\"Çářôℓ\",\"Çôçôñúƭ\",\"Accepted, not confirmed yet\",\"\",\"Torchic\",,,\"G0\"\r\n\"5\",\"daniel@example.org\",,,\"Created\",\"\",\"\",,,\"\"\r\n";
        var csvText = Encoding.UTF8.GetString(csvBytes[3..]);
        Assert.AreEqual(expected, csvText);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExportParticipantFilesHasExpectedContent(bool onlyConfirmed)
    {
        var form = new ProfileForm([], [
            new ProfileFormFile("Resume", "...", false, [".pdf"]),
            new ProfileFormFile("Photo", ".....", true, ["", ".jpg", ".jpeg"])
        ]);
        var aliceResume = new File.InMemory("alice-resume.pdf", "application/pdf", [10, 20, 40, 80]);
        var alicePhoto = new File.InMemory("photo of alice", "image/png", [0]); // ensure lack of extension isn't a problem
        var bobPhoto = new File.InMemory("photo bob.jpeg", "image/jpeg", [1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 1]);
        {
            var aliceResumeId = await FileStorage.StoreFileAsync(aliceResume);
            var alicePhotoId = await FileStorage.StoreFileAsync(alicePhoto);
            var bobPhotoId = await FileStorage.StoreFileAsync(bobPhoto);
            // add in two phases to preserve order, we want to test ordering as well
            Db.Participants.Add(new("bob@example.org")
            {
                GivenName = "Bob",
                FamilyName = "Ba Nana",
                Profile = ImmutableDictionary<string, string>.Empty.Add("Photo", bobPhotoId),
                Status = ParticipantStatus.Finalized
            });
            Db.Participants.Add(new("alice@example.org")
            {
                GivenName = "Aℓïçè",
                FamilyName = "Âƥƥℓè",
                Profile = ImmutableDictionary<string, string>.Empty.Add("Resume", aliceResumeId)
                                                                   .Add("Photo", alicePhotoId),
                Status = ParticipantStatus.CheckedIn
            });
            await Db.CommitAsync();
        }

        var page = new BackupPage(Db, EventDetails, form, FileStorage, DisabledTimeProvider);
        var zip = await page.ExportParticipantFilesAsync(onlyConfirmed);
        await using var zipStream = zip.OpenRead();
        await using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.HasCount(onlyConfirmed ? 2 : 3, zipArchive.Entries);

        async Task AssertHasContentsAsync(string name, byte[] contents)
        {
            var entry = zipArchive.Entries.First(e => e.Name.Equals(name, StringComparison.Ordinal));
            await using var stream = await entry.OpenAsync();
            using var memoryStream = new System.IO.MemoryStream();
            await stream.CopyToAsync(memoryStream);
            Assert.AreSequenceEqual(contents, memoryStream.ToArray());
        }

        await AssertHasContentsAsync("1 Aℓïçè Âƥƥℓè Resume.pdf", aliceResume.Contents);
        await AssertHasContentsAsync("1 Aℓïçè Âƥƥℓè Photo", alicePhoto.Contents);
        if (!onlyConfirmed)
        {
            await AssertHasContentsAsync("2 Bob Ba Nana Photo.jpeg", bobPhoto.Contents);
        }
    }

    // Backup import-export is tested in e2e tests, we only check edge cases here:

    [TestMethod]
    public async Task ImportFailsForEmptyButValidArchive()
    {
        using var memoryStream = new System.IO.MemoryStream();
        await using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Nothing.
        }
        var emptyFile = new File.InMemory($"backup{Backup.FileExtension}", "application/zip", memoryStream.ToArray());

        var page = new BackupPage(Db, EventDetails, new([], []), FileStorage, DisabledTimeProvider);
        var admin = await GetAdminAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.ImportAsync(admin, emptyFile));
    }

    [TestMethod]
    public async Task ImportFailsWithWrongFileExtension()
    {
        var page = new BackupPage(Db, EventDetails, new([], []), FileStorage, DisabledTimeProvider);
        var file = new File.InMemory("not-the-right.file-extension", "application/octet-stream", [0, 1, 2, 3]);

        var admin = await GetAdminAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.ImportAsync(admin, file));
    }

    [TestMethod]
    public async Task ImportRecreatesAdminAccountIfNeeded()
    {
        var page = new BackupPage(Db, EventDetails, new([], []), FileStorage, TimeProvider);
        var backup = await page.ExportAsync();
        var backupBytes = await backup.ReadAsBytesAsync();

        await Db.DisposeAsync();
        await ReInitializeDbAsync();

        Db.Admins.Add(new("new-admin@example.org") { IsEmailAddressVerified = true, IsOwner = true });
        await Db.CommitAsync();

        var newAdmin = await Db.Admins.FindAsync("new-admin@example.org");
        Assert.IsNotNull(newAdmin);
        var savedBackup = new File.InMemory(backup.Name, backup.MimeType, backupBytes);

        // must recreate the page since we changed Db!
        page = new BackupPage(Db, EventDetails, new([], []), FileStorage, TimeProvider);
        var result = await page.ImportAsync(newAdmin, savedBackup);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var newAdmin2 = await Db.Admins.FindAsync("new-admin@example.org");
        Assert.IsNotNull(newAdmin2);
        Assert.IsTrue(newAdmin2.IsOwner);
        Assert.IsTrue(newAdmin2.IsEmailAddressVerified);
    }
}