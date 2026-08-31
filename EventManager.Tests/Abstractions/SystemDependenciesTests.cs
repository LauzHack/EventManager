using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Abstractions;

[TestClass]
public sealed class SystemDependenciesTests : TestsBase
{
    [TestMethod]
    public async Task UsesDatabase()
    {
        var dependencies = await CreateDependenciesAsync();

        var page = (DatabasePage)await dependencies.CreatePageAsync(typeof(DatabasePage));
        Assert.AreSame(Db, page.Database);
    }

    [TestMethod]
    public async Task UsesFileStorage()
    {
        var dependencies = await CreateDependenciesAsync();

        var page = (FileStoragePage)await dependencies.CreatePageAsync(typeof(FileStoragePage));
        Assert.AreSame(FileStorage, page.Storage);
    }

    [TestMethod]
    public async Task UsesEmailSender()
    {
        var dependencies = await CreateDependenciesAsync();

        var page = (EmailSenderPage)await dependencies.CreatePageAsync(typeof(EmailSenderPage));
        Assert.AreSame(EmailSender, page.Sender);
    }

    [TestMethod]
    public async Task UsesTimeProvider()
    {
        var dependencies = await CreateDependenciesAsync();

        var page = (TimeProviderPage)await dependencies.CreatePageAsync(typeof(TimeProviderPage));
        Assert.AreSame(TimeProvider, page.Provider);
    }

    [TestMethod]
    public async Task UsesDbForIQueryable()
    {
        Db.Participants.Add(new Participant("alice@example.org"));
        await Db.CommitAsync();

        var dependencies = await CreateDependenciesAsync();

        var page = (ReadOnlyDatabasePage)await dependencies.CreatePageAsync(typeof(ReadOnlyDatabasePage));
        var actual = Assert.ContainsSingle(await page.Participants.ToCollectionAsync());
        Assert.AreEqual("alice@example.org", actual.EmailAddress);
    }

    [TestMethod]
    public async Task UsesDbForDbValues()
    {
        Db.Participants.Add(new Participant("alice@example.org"));
        await Db.CommitAsync();

        var dependencies = await CreateDependenciesAsync();

        var page = (WritableDatabasePage)await dependencies.CreatePageAsync(typeof(WritableDatabasePage));
        var actual = Assert.ContainsSingle(await page.Participants.ToCollectionAsync());
        Assert.AreEqual("alice@example.org", actual.EmailAddress);
    }

    [TestMethod]
    public async Task UsesConfigForProperty()
    {
        var data = new LetterData("Somewhere", "fr-CH", "Someone", "x@example.org", "123");

        var dependencies = await CreateDependenciesAsync();
        dependencies.Configuration.Set(data);
        await dependencies.Database.CommitAsync();

        var page = (ReadOnlyConfiguredPage)await dependencies.CreatePageAsync(typeof(ReadOnlyConfiguredPage));
        Assert.AreEqual(data, page.Data);
    }

    [TestMethod]
    public async Task UsesPresentConfigForOptionalProperty()
    {
        var data = new LetterData("Somewhere", "fr-CH", "Someone", "x@example.org", "123");

        var dependencies = await CreateDependenciesAsync();
        dependencies.Configuration.Set(data);
        await dependencies.Database.CommitAsync();

        var page = (OptionalReadOnlyConfiguredPage)await dependencies.CreatePageAsync(typeof(OptionalReadOnlyConfiguredPage));
        Assert.AreEqual(data, page.Data);
    }

    [TestMethod]
    public async Task UsesAbsentConfigForOptionalProperty()
    {
        var dependencies = await CreateDependenciesAsync();

        var page = (OptionalReadOnlyConfiguredPage)await dependencies.CreatePageAsync(typeof(OptionalReadOnlyConfiguredPage));
        Assert.IsNull(page.Data);
    }

    [TestMethod]
    public async Task UsesConfigForConfigValue()
    {
        var data = new LetterData("Somewhere", "fr-CH", "Someone", "x@example.org", "123");

        var dependencies = await CreateDependenciesAsync();
        dependencies.Configuration.Set(data);
        await dependencies.Database.CommitAsync();

        var page = (WritableConfiguredPage)await dependencies.CreatePageAsync(typeof(WritableConfiguredPage));
        Assert.AreEqual(data, page.Data.Value);
    }

    [TestMethod]
    public async Task SetsOptionalDependenciesToNullWhenAbsent()
    {
        var dependencies = await CreateDependenciesAsync();

        var dependent = (OptionallyDependentPage)await dependencies.CreatePageAsync(typeof(OptionallyDependentPage));
        Assert.IsNull(dependent.Dependency);
    }

    [TestMethod]
    public async Task FailsWhenDependencyIsUnknown()
    {
        var dependencies = await CreateDependenciesAsync();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => dependencies.CreatePageAsync(typeof(DependentPage)));
    }

    [TestMethod]
    public async Task FailsWhenThereIsMoreThanOneConstructor()
    {
        var dependencies = await CreateDependenciesAsync();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => dependencies.CreatePeriodicTaskAsync(typeof(AmbiguousDependentTask)));
    }

    [TestMethod]
    public async Task FailsIfConfigIsNotSetForProperty()
    {
        var dependencies = await CreateDependenciesAsync();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => dependencies.CreatePageAsync(typeof(ReadOnlyConfiguredPage)));
    }

    [TestMethod]
    public async Task FailsGracefullyInPresenceOfCycles()
    {
        var dependencies = await CreateDependenciesAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => dependencies.CreatePageAsync(typeof(RecursiveDependentPage)));
    }

    private Task<SystemDependencies> CreateDependenciesAsync()
        => SystemDependencies.CreateAsync(Db, FileStorage, c => EmailSender, TimeProvider);

    private sealed class FakeUser() : User
    {
        public override string Id => "fake@example.org";
    }

    private interface IDependency;
    private sealed class Dependent(IDependency dependency)
    {
        public IDependency Dependency { get; } = dependency;
    }

    private sealed class DependentPage(IDependency dependency) : Page<FakeUser>
    {
        public IDependency Dependency { get; } = dependency;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class NestedDependentPage(Dependent dependent) : Page<FakeUser>
    {
        public Dependent Dependent { get; } = dependent;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class OptionallyDependentPage(IDependency? dependency) : Page<FakeUser>
    {
        public IDependency? Dependency { get; } = dependency;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class AmbiguousDependentTask(IDependency? dependency) : PeriodicTask
    {
        public IDependency? Dependency { get; } = dependency;

        public AmbiguousDependentTask() : this(null) { }

        public override async Task<string?> RunAsync()
            => "Hello";
    }

    private sealed class DatabasePage(Db database) : Page<FakeUser>
    {
        public Db Database { get; } = database;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class FileStoragePage(FileStorage storage) : Page<FakeUser>
    {
        public FileStorage Storage { get; } = storage;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class EmailSenderPage(EmailSender sender) : Page<FakeUser>
    {
        public EmailSender Sender { get; } = sender;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class TimeProviderPage(TimeProvider provider) : Page<FakeUser>
    {
        public TimeProvider Provider { get; } = provider;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class ReadOnlyDatabasePage(IQueryable<Participant> participants) : Page<FakeUser>
    {
        public IQueryable<Participant> Participants { get; } = participants;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class WritableDatabasePage(DbValues<Participant> participants) : Page<FakeUser>
    {
        public DbValues<Participant> Participants { get; } = participants;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class ReadOnlyConfiguredPage(LetterData data) : Page<FakeUser>
    {
        public LetterData Data { get; } = data;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class OptionalReadOnlyConfiguredPage(LetterData? data) : Page<FakeUser>
    {
        public LetterData? Data { get; } = data;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed class WritableConfiguredPage(ConfigValue<LetterData> data) : Page<FakeUser>
    {
        public ConfigValue<LetterData> Data { get; } = data;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }

    private sealed record RecursiveDependent1(RecursiveDependent2 Other);
    private sealed record RecursiveDependent2(RecursiveDependent1 Other);
    private sealed class RecursiveDependentPage(RecursiveDependent1 dependency) : Page<FakeUser>
    {
        public RecursiveDependent1 Dependency { get; } = dependency;

        public override async Task<PageView> ViewAsync(FakeUser user)
            => RequiredView("Title");
    }
}