using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Web;

public sealed class EntityFrameworkDb : Db
{
    private readonly string _fileName;
    private EntityFrameworkContext? _context;

    private EntityFrameworkContext Context
        => _context is null ? throw new ObjectDisposedException(null) : _context;

    public override DbValues<Admin> Admins
        => new EntityFrameworkValues<Admin>(Context.Admins);

    public override DbValues<ApplicationGroup> ApplicationGroups
        => new EntityFrameworkValues<ApplicationGroup>(Context.ApplicationGroups);

    public override DbValues<AuditMessage> AuditMessages
        => new EntityFrameworkValues<AuditMessage>(Context.AuditMessages);

    public override DbValues<Award> Awards
        => new EntityFrameworkValues<Award>(Context.Awards);

    public override DbValues<ChallengeSetter> ChallengeSetters
        => new EntityFrameworkValues<ChallengeSetter>(Context.ChallengeSetters);

    public override DbValues<StoredConfigValue> ConfigValues
        => new EntityFrameworkValues<StoredConfigValue>(Context.ConfigValues);

    public override DbValues<Currency> Currencies
        => new EntityFrameworkValues<Currency>(Context.Currencies);

    public override DbValues<Letter> Letters
        => new EntityFrameworkValues<Letter>(Context.Letters);

    public override DbValues<Participant> Participants
        => new EntityFrameworkValues<Participant>(Context.Participants);

    public override DbValues<Project> Projects
        => new EntityFrameworkValues<Project>(Context.Projects);

    public override DbValues<TravelExpense> TravelExpenses
        => new EntityFrameworkValues<TravelExpense>(Context.TravelExpenses);

    public EntityFrameworkDb(string fileName)
    {
        _fileName = fileName;
        _context = CreateContext(_fileName);
    }

    public override Task InitializeAsync()
        => Context.Database.EnsureCreatedAsync();

    public override async Task<bool> CommitAsync()
    {
        if (_context is null)
        {
            // As per the method's contract, it's OK to commit no changes on a disposed instance
            return false;
        }

        var result = await Context.SaveChangesAsync() > 0;
#if DEBUG
        // Ensure we don't accidentally depend on the context caching stuff in tests
        await DisposeAsync(releaseFile: true);
        _context = CreateContext(_fileName);
#endif
        return result;
    }

    public override void EnsureNoChanges()
    {
        if (_context is not null && _context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("No changes were expected");
        }
    }

    public override void CancelChanges()
        => _context?.ChangeTracker.Clear();

    public override async Task<Stream> ExportAndDisposeAsync()
    {
        await DisposeAsync(releaseFile: true);
        return System.IO.File.OpenRead(_fileName);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Using custom Dispose method to ensure the file is released")]
    public override async Task OverwriteAsync(Stream stream)
    {
        // First, create it in a temporary file to check it's a valid database.
        var tempFile = new FileInfo(_fileName + ".tmp");
        // This must have a scope that ends before we move the file, otherwise we won't be able to move it
        await using (var localFileStream = tempFile.OpenWrite())
        {
            await stream.CopyToAsync(localFileStream);
        }
        // Hopefully checking the migrations is enough to point out any schema mismatches.
        var tempDb = new EntityFrameworkDb(tempFile.Name);
        try
        {
            var migrations = await tempDb.Context.Database.GetPendingMigrationsAsync();
            if (migrations.Any())
            {
                throw new InvalidOperationException("Mismatched database schemas. Import canceled.");
            }
        }
        finally
        {
            await tempDb.DisposeAsync(releaseFile: true);
        }

        await DisposeAsync(releaseFile: true);
        tempFile.MoveTo(_fileName, overwrite: true);
        _context = CreateContext(_fileName);
        TriggerConfigValuesOverwritten(await ConfigValues.ToArrayAsync());
    }

#if DEBUG
    // Ensure tests can delete DB files whenever
    public override ValueTask DisposeAsync()
        => DisposeAsync(releaseFile: true);
#else
    public override ValueTask DisposeAsync()
        => DisposeAsync(releaseFile: false);
#endif

    private async ValueTask DisposeAsync(bool releaseFile)
    {
        if (_context is not null)
        {
            if (_context.ChangeTracker.HasChanges())
            {
                throw new InvalidOperationException("Cannot dispose a database with pending changes. Call CancelChanges explicitly first.");
            }
            if (releaseFile)
            {
                SqliteConnection.ClearPool((SqliteConnection)_context.Database.GetDbConnection());
            }
            await _context.DisposeAsync();
            _context = null;
        }
    }

    private static EntityFrameworkContext CreateContext(string fileName)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EntityFrameworkContext>();
        optionsBuilder.UseSqlite($"DataSource={fileName}");
        return new EntityFrameworkContext(optionsBuilder.Options);
    }

    private sealed class EntityFrameworkValues<T>(DbSet<T> set) : DbValues<T>, IAsyncEnumerable<T>
        where T : class
    {
        private IQueryable<T> Queryable
            => set;

        public override Expression Expression
            => Queryable.Expression;

        public override IQueryProvider Provider
            => Queryable.Provider;

        public override void Add(T item)
            => set.Add(item);

        public override void Remove(T item)
            => set.Remove(item);

        public override Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
            => predicate is null ? set.CountAsync() : set.CountAsync(predicate);

        public override ValueTask<T?> FindAsync(string key)
            => set.FindAsync(key);

        public override IEnumerator<T> GetEnumerator()
            => Queryable.GetEnumerator();

        public override IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => ((IAsyncEnumerable<T>)Queryable).GetAsyncEnumerator(cancellationToken);
    }

    private sealed class EntityFrameworkContext(DbContextOptions<EntityFrameworkContext> options) : DbContext(options)
    {
        public DbSet<Admin> Admins { get; set; }

        public DbSet<ApplicationGroup> ApplicationGroups { get; set; }

        public DbSet<AuditMessage> AuditMessages { get; set; }

        public DbSet<Award> Awards { get; set; }

        public DbSet<ChallengeSetter> ChallengeSetters { get; set; }

        public DbSet<StoredConfigValue> ConfigValues { get; set; }

        public DbSet<Currency> Currencies { get; set; }

        public DbSet<Letter> Letters { get; set; }

        public DbSet<Participant> Participants { get; set; }

        public DbSet<Project> Projects { get; set; }

        public DbSet<TravelExpense> TravelExpenses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            const string collation = "NOCASE";

            modelBuilder.Entity<Admin>().HasKey(a => a.EmailAddress);
            modelBuilder.Entity<Admin>().Property(a => a.EmailAddress).UseCollation(collation);

            modelBuilder.Entity<ApplicationGroup>().HasKey(g => g.Id);
            modelBuilder.Entity<ApplicationGroup>().Property(p => p.Id).UseCollation(collation);
            modelBuilder.Entity<ApplicationGroup>().HasMany(g => g.Members).WithOne();
            modelBuilder.Entity<ApplicationGroup>().Navigation(g => g.Members).AutoInclude();
            modelBuilder.Entity<ApplicationGroup>().HasMany(g => g.InvitedParticipants).WithMany();
            modelBuilder.Entity<ApplicationGroup>().Navigation(g => g.InvitedParticipants).AutoInclude();

            // EF Core supports "keyless entities" but only for views, it cannot append them to a table, so we need a fake ID
            modelBuilder.Entity<AuditMessage>().Property<int>("Id").ValueGeneratedOnAdd();
            modelBuilder.Entity<AuditMessage>().HasKey("Id");

            // Ditto re: keyless entities
            modelBuilder.Entity<Award>().Property<int>("Id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Award>().HasKey("Id");

            modelBuilder.Entity<ChallengeSetter>().HasKey(c => c.Name);
            modelBuilder.Entity<ChallengeSetter>().Property(c => c.Name).UseCollation(collation);
            modelBuilder.Entity<ChallengeSetter>().HasMany(c => c.Awards).WithOne();
            modelBuilder.Entity<ChallengeSetter>().Navigation(c => c.Awards).AutoInclude();

            modelBuilder.Entity<StoredConfigValue>().HasKey(c => c.TypeName);

            modelBuilder.Entity<Currency>().HasKey(c => c.Code);
            modelBuilder.Entity<Currency>().Property(a => a.Code).UseCollation(collation);

            modelBuilder.Entity<Letter>().Property(l => l.Id).ValueGeneratedNever();

            modelBuilder.Entity<Participant>().HasKey(p => p.EmailAddress);
            modelBuilder.Entity<Participant>().Property(p => p.EmailAddress).UseCollation(collation);
            modelBuilder.Entity<Participant>().Property(p => p.FutureEmailAddress).UseCollation(collation);
            modelBuilder.Entity<Participant>().Property(p => p.GivenName).UseCollation(collation);
            modelBuilder.Entity<Participant>().Property(p => p.FamilyName).UseCollation(collation);
            modelBuilder.Entity<Participant>()
                           .Property(p => p.Profile)
                           .HasConversion(
                               v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                               v => JsonSerializer.Deserialize<ImmutableDictionary<string, string>>(v, JsonSerializerOptions.Default)!
                           );
            // This should be a complex property but EF Core doesn't support them having navigations yet: https://github.com/dotnet/efcore/issues/31245
            modelBuilder.Entity<Participant>().OwnsOne(p => p.VisaInformation).Navigation(p => p.Letter).AutoInclude();

            modelBuilder.Entity<Project>().HasKey(g => g.Id);
            modelBuilder.Entity<Project>().Property(p => p.Id).UseCollation(collation);
            modelBuilder.Entity<Project>().Property(p => p.Title).UseCollation(collation);
            modelBuilder.Entity<Project>().HasMany(p => p.Team).WithOne();
            modelBuilder.Entity<Project>().Navigation(p => p.Team).AutoInclude();
            modelBuilder.Entity<Project>().HasMany(p => p.InvitedParticipants).WithMany();
            modelBuilder.Entity<Project>().Navigation(p => p.InvitedParticipants).AutoInclude();

            modelBuilder.Entity<TravelExpense>().HasKey(t => t.ReceiptId);
            modelBuilder.Entity<TravelExpense>().HasMany(t => t.Owners).WithMany();
            modelBuilder.Entity<TravelExpense>().Navigation(t => t.Owners).AutoInclude();
        }
    }
}