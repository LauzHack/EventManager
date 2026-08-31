using System;
using System.Collections;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;
using EventManager.Web;

using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests;

[TestClass]
public sealed class EntityFrameworkDbTests
{
    private Db _db = null!;
    private string _filePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _filePath = System.IO.Path.GetTempFileName();
        _db = new EntityFrameworkDb(_filePath);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _db.DisposeAsync();
        SqliteConnection.ClearAllPools(); // otherwise the file can't be deleted
        System.IO.File.Delete(_filePath);
    }

    [TestMethod]
    public async Task CommitChangesCommitsAdditions()
    {
        await _db.InitializeAsync();

        _db.Admins.Add(new Admin("first@example.org"));
        _db.Admins.Add(new Admin("second@example.org"));
        await _db.CommitAsync();

        var admins = await _db.Admins.ToCollectionAsync();
        Assert.AreSequenceEqual(["first@example.org", "second@example.org"], admins.Select(a => a.EmailAddress));
    }

    [TestMethod]
    public async Task CommitChangesCommitsRemovals()
    {
        await _db.InitializeAsync();
        var first = new Admin("first@example.org");

        _db.Admins.Add(first);
        _db.Admins.Add(new Admin("second@example.org"));
        await _db.CommitAsync();

        _db.Admins.Remove(first);
        await _db.CommitAsync();

        var admins = await _db.Admins.ToCollectionAsync();
        var admin = Assert.ContainsSingle(admins);
        Assert.AreEqual("second@example.org", admin.EmailAddress);
    }

    [TestMethod]
    public async Task CancelChangesNullifiesAdditions()
    {
        await _db.InitializeAsync();

        _db.Admins.Add(new Admin("first@example.org"));
        _db.Admins.Add(new Admin("second@example.org"));
        _db.CancelChanges();
        _db.Admins.Add(new Admin("third@example.org"));
        await _db.CommitAsync();

        var admins = await _db.Admins.ToCollectionAsync();
        var admin = Assert.ContainsSingle(admins);
        Assert.AreEqual("third@example.org", admin.EmailAddress);
    }

    [TestMethod]
    public async Task CancelChangesNullifiesRemovals()
    {
        await _db.InitializeAsync();
        var first = new Admin("first@example.org");

        _db.Admins.Add(first);
        await _db.CommitAsync();
        _db.Admins.Remove(first);
        _db.CancelChanges();
        _db.Admins.Add(new Admin("second@example.org"));
        await _db.CommitAsync();

        var admins = await _db.Admins.ToCollectionAsync();
        Assert.AreSequenceEqual(["first@example.org", "second@example.org"], admins.Select(a => a.EmailAddress));
    }

    [TestMethod]
    public async Task WhereFilters()
    {
        await _db.InitializeAsync();

        _db.Admins.Add(new Admin("first@example.org"));
        _db.Admins.Add(new Admin("second@example.org"));
        await _db.CommitAsync();

        var admins = await _db.Admins.Where(a => a.EmailAddress.Contains("first")).ToCollectionAsync();
        var admin = Assert.ContainsSingle(admins);
        Assert.AreEqual("first@example.org", admin.EmailAddress);
    }

    [TestMethod]
    public async Task CountAsyncCanIncludeFilter()
    {
        await _db.InitializeAsync();

        _db.Admins.Add(new Admin("first@example.org"));
        _db.Admins.Add(new Admin("second@example.org"));
        await _db.CommitAsync();

        var count = await _db.Admins.CountAsync(a => a.EmailAddress.Contains("first"));
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task FindAsyncFindsExistingValue()
    {
        await _db.InitializeAsync();

        _db.Admins.Add(new Admin("first@example.org"));
        _db.Admins.Add(new Admin("second@example.org"));
        await _db.CommitAsync();

        var lone = await _db.Admins.FindAsync("second@example.org");
        Assert.IsNotNull(lone);
        Assert.AreEqual("second@example.org", lone.EmailAddress);
    }

    [TestMethod]
    public async Task FindAsyncDoesNotFindMissingValue()
    {
        await _db.InitializeAsync();

        _db.Admins.Add(new Admin("first@example.org"));
        _db.Admins.Add(new Admin("second@example.org"));
        await _db.CommitAsync();

        var missing = await _db.Admins.FindAsync("third@example.org");
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task ParticipantRoundtrips()
    {
        var originalDate = DateTimeOffset.UtcNow;
        var original = new Participant("alice@example.org")
        {
            GivenName = "Alice",
            FamilyName = "Apple",
            Referrer = "Somewhere",
            AdminRemarks = "This participant is just a test",
            LastStatusReminderDate = DateTimeOffset.MaxValue,
            Status = ParticipantStatus.CheckedIn,
            Profile = ImmutableDictionary<string, string>.Empty.Add("x", "y").Add("a\0b\u0001c", "d\0efg\u0002hi "),
            VisaInformation = new ParticipantVisaInformation
            {
                Letter = new Letter("id", "Hello", DateTimeOffset.MinValue.AddMinutes(123)),
                ParticipantDetails = ["X", "Y"],
                AdminDetails = "Alice Anne Apple",
                PassportPhotoId = "xyz"
            },
            TravelReimbursementTier = "abc"
        };

        await _db.InitializeAsync();
        _db.Participants.Add(original);
        _db.TravelExpenses.Add(new("111", originalDate, "Expense", 42.1m, "PLN", true) { Owners = { original }, Status = TravelExpenseStatus.Approved });
        await _db.CommitAsync();
        await _db.DisposeAsync();
        _db = new EntityFrameworkDb(_filePath);

        var stored = await _db.Participants.FindAsync(original.EmailAddress);
        Assert.IsNotNull(stored);
        Assert.AreNotSame(original, stored);
        Assert.AreEqual(original.EmailAddress, stored.EmailAddress);
        Assert.AreEqual(original.AdminRemarks, stored.AdminRemarks);
        Assert.AreEqual(original.GivenName, stored.GivenName);
        Assert.AreEqual(original.FamilyName, stored.FamilyName);
        Assert.AreEqual(original.Referrer, stored.Referrer);
        Assert.AreEqual(original.LastStatusReminderDate, stored.LastStatusReminderDate);
        Assert.AreEqual(original.Status, stored.Status);
        Assert.AreSequenceEqual(original.Profile, stored.Profile, SequenceOrder.InAnyOrder);
        Assert.IsNotNull(stored.VisaInformation.Letter);
        Assert.AreEqual(original.VisaInformation.Letter.Body, stored.VisaInformation.Letter.Body);
        Assert.AreEqual(original.VisaInformation.Letter.DateTime, stored.VisaInformation.Letter.DateTime);
        Assert.AreSequenceEqual(original.VisaInformation.ParticipantDetails, stored.VisaInformation.ParticipantDetails);
        Assert.AreEqual(original.VisaInformation.AdminDetails, stored.VisaInformation.AdminDetails);
        Assert.AreEqual(original.VisaInformation.PassportPhotoId, stored.VisaInformation.PassportPhotoId);
        Assert.AreEqual(original.TravelReimbursementTier, stored.TravelReimbursementTier);
        var expense = Assert.ContainsSingle(_db.TravelExpenses);
        Assert.AreEqual("111", expense.ReceiptId);
        Assert.AreEqual(originalDate, expense.CreationDate);
        Assert.AreEqual("Expense", expense.Description);
        Assert.AreEqual(42.1m, expense.Amount);
        Assert.AreEqual("PLN", expense.CurrencyCode);
        Assert.IsTrue(expense.CountsDouble);
        Assert.AreEqual(TravelExpenseStatus.Approved, expense.Status);
    }

    [TestMethod]
    public async Task ProjectRoundtrips()
    {
        await _db.InitializeAsync();
        var participant = new Participant("alice@example.org");
        var invited1 = new Participant("invited@example.org");
        var invited2 = new Participant("other@example.org");
        var original = new Project("project id", "Some project", "Do cool stuff", "Really do cool stuff, explained longer", "https://example.org", "xxx")
        {
            Challenges = ["first", "second"],
            InvitedParticipants = { invited1, invited2 },
            Team = { participant }
        };

        _db.Participants.Add(participant, invited1, invited2);
        _db.Projects.Add(original);

        await _db.CommitAsync();
        await _db.DisposeAsync();
        _db = new EntityFrameworkDb(_filePath);

        var roundtrippedParticipant = await _db.Participants.FindAsync(participant.EmailAddress);
        Assert.IsNotNull(roundtrippedParticipant);

        var roundtrippedProject = await _db.Projects.FirstOrDefaultAsync(p => p.Team.Contains(roundtrippedParticipant));
        Assert.IsNotNull(roundtrippedProject);
        Assert.AreEqual(original.Title, roundtrippedProject.Title);
        Assert.AreEqual(original.ShortDescription, roundtrippedProject.ShortDescription);
        Assert.AreEqual(original.LongDescription, roundtrippedProject.LongDescription);
        Assert.AreEqual(original.Link, roundtrippedProject.Link);
        Assert.AreSequenceEqual(original.Challenges, roundtrippedProject.Challenges);
        Assert.AreEqual(original.ThumbnailId, roundtrippedProject.ThumbnailId);
        Assert.AreSequenceEqual(original.Team.Select(p => p.EmailAddress), roundtrippedProject.Team.Select(p => p.EmailAddress));
        Assert.AreSequenceEqual(original.InvitedParticipants.Select(p => p.EmailAddress), roundtrippedProject.InvitedParticipants.Select(p => p.EmailAddress));
    }

    [TestMethod]
    public async Task LetterRoundtrips()
    {
        await _db.InitializeAsync();
        var original = new Letter("id", "Hello World!", DateTimeOffset.MinValue.AddMinutes(456));

        _db.Letters.Add(original);

        await _db.CommitAsync();
        await _db.DisposeAsync();
        _db = new EntityFrameworkDb(_filePath);

        var roundtripped = await _db.Letters.FindAsync(original.Id);
        Assert.IsNotNull(roundtripped);
        Assert.AreEqual(original.Body, roundtripped.Body);
        Assert.AreEqual(original.DateTime, roundtripped.DateTime);
    }

    [TestMethod]
    public async Task AuditMessageRoundtrips()
    {
        await _db.InitializeAsync();
        var original = new AuditMessage(Status.UserError, "You did a bad thing!", "someone@example.org", "System", DateTimeOffset.MinValue.AddMinutes(789));

        _db.AuditMessages.Add(original);

        await _db.CommitAsync();
        await _db.DisposeAsync();
        _db = new EntityFrameworkDb(_filePath);

        var roundtripped = await _db.AuditMessages.ToCollectionAsync();
        var single = Assert.ContainsSingle(roundtripped);
        Assert.AreEqual(original.Status, single.Status);
        Assert.AreEqual(original.Text, single.Text);
        Assert.AreEqual(original.EmailAddress, single.EmailAddress);
        Assert.AreEqual(original.Source, single.Source);
        Assert.AreEqual(original.DateTime, single.DateTime);
    }

    [TestMethod]
    public async Task ElementTypeIsCorrect()
    {
        await _db.InitializeAsync();
        Assert.AreEqual(typeof(AuditMessage), _db.AuditMessages.ElementType);
    }

    [TestMethod]
    public async Task NonGenericEnumeratorWorks()
    {
        await _db.InitializeAsync();
        var original = new AuditMessage(Status.UserError, "You did a bad thing!", "someone@example.org", "System", DateTimeOffset.MinValue.AddMinutes(789));
        _db.AuditMessages.Add(original);
        await _db.CommitAsync();

        var nonGeneric = (IEnumerable)_db.AuditMessages;
        bool first = true;
        foreach (var item in nonGeneric)
        {
            Assert.IsTrue(first);
            Assert.AreEqual(original, item);
            first = false;
        }
    }
}