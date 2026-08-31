using System;
using System.Collections.Immutable;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.Web;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.TestInfrastructure;

public abstract class TestsBase
{
    private string _dbFilePath = null!;
    private string _storageRootPath = null!;

    protected static readonly EventDetails EventDetails = new("Event", "Somewhere", "UTC", new(2025, 11, 22, 8, 0, 0, TimeSpan.Zero), new(2025, 11, 23, 17, 0, 0, TimeSpan.Zero), "Hello!", new("https://example.org/main"), new("https://example.org/help"), "Everything's private");
    protected static readonly EventTheme EventTheme = new(RgbColor.Black, RgbColor.White, "fake_logo_id", "fake_icon_id", "image/png");
    protected static readonly EventLimits EventLimits = new(4, 6, 30, 7);
    protected static readonly LetterData LetterData = new("Nowhere", "fr-CH", "Someone", "someone@example.org", "123");
    protected static readonly VisaInvitationFormat VisaInvitationFormat = new("Hello $NAME, $DETAILS", ["Home address", "Phone number"], "Name");
    protected static readonly AuthenticationSecret AuthSecret = new([0, 1, 2, 3]);
    protected static readonly TravelReimbursementPolicy ReimbursementPolicy = new("CHF", "Tiers", new("https://example.org"), ImmutableDictionary.CreateRange<string, decimal>([new("A", 999m), new("B", 1111m)]), 5);

    protected Db Db { get; private set; } = null!;
    protected FakeEmailSender EmailSender { get; private set; } = null!;
    protected FakeEmailSender DisabledEmailSender { get; private set; } = null!;
    protected FileStorage FileStorage { get; private set; } = null!;
    protected TimeProvider TimeProvider { get; private set; } = new FakeTimeProvider(new DateTimeOffset(3030, 1, 2, 3, 4, 5, 6, TimeSpan.Zero));
    protected TimeProvider DisabledTimeProvider { get; private set; } = new FakeTimeProvider(null);

    [TestInitialize]
    public async Task Initialize()
    {
        _dbFilePath = System.IO.Path.GetTempFileName();
        _storageRootPath = System.IO.Directory.CreateTempSubdirectory("FileStorage").FullName;

        await ReInitializeDbAsync();

        EmailSender = new FakeEmailSender();
        DisabledEmailSender = new FakeEmailSender(enabled: false);
        FileStorage = new DiskFileStorage(_storageRootPath);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        Db.CancelChanges();
        await Db.DisposeAsync();
        System.IO.File.Delete(_dbFilePath);
        System.IO.Directory.Delete(_storageRootPath, true);
    }

    protected async Task ReInitializeDbAsync()
    {
        Db = new EntityFrameworkDb(_dbFilePath);
        await Db.InitializeAsync();
    }

    protected async Task SetConfigValueAsync(object value)
    {
        var config = await Config.CreateAsync(Db);
        config.Set(value);
        await Db.CommitAsync();
    }
}