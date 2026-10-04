using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Infrastructure.Tests;

[TestClass]
public class SqliteDbContextProviderTests
{
    [TestMethod]
    public void CreateContext_ReturnsValidContext()
    {
        using var provider = new SqliteDbContextProvider();
        using var context = provider.CreateContext();

        Assert.IsNotNull(context);
    }

    [TestMethod]
    public void CreateContext_MultipleCalls_ReturnDistinctContexts()
    {
        using var provider = new SqliteDbContextProvider();

        using var context1 = provider.CreateContext();
        using var context2 = provider.CreateContext();

        Assert.AreNotSame(context1, context2);
        Assert.AreNotSame(context1.Database.GetDbConnection(), context2.Database.GetDbConnection());
    }

    [TestMethod]
    public async Task Migrator_CreatesSchemaAndMigrationHistory()
    {
        using var provider = new SqliteDbContextProvider();
        var migrator = CreateMigrator(provider);

        await migrator.MigrateAsync();

        await using var context = provider.CreateContext();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.IsTrue(applied.Any(x => x.EndsWith("_InitialEventStore", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddOwnerAliasAdministration", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddOwnerDisplayName", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddHorseIdentityRepair", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddSubjectIdentificationRepairIssues", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RaceIdentityLookupIndex_UpgradesPreviousMigrationWithoutChangingRaceRows_AndIsIdempotent()
    {
        const string previousMigration = "20260921074403_AddRaceRescheduleLineage";
        const string newMigration = "20261004163306_AddRaceIdentityLookupIndex";
        using var provider = new SqliteDbContextProvider();
        await using (var previous = provider.CreateContext())
        {
            await previous.Database.MigrateAsync(previousMigration);
            await previous.Database.ExecuteSqlRawAsync("""
                INSERT INTO RacePredictionContexts
                    (RaceId, RaceDate, RacecourseCode, RaceNumber, RaceName, Status, Entries,
                     WeatherObservations, TrackConditionObservations, OddsSnapshots)
                VALUES
                    ('race-upgrade-tokyo', '2037-09-27', 'TOKYO', 11, 'Preserved Tokyo', 0, '[]', '[]', '[]', '[]'),
                    ('race-upgrade-nakayama', '2037-09-27', 'NAKAYAMA', 11, 'Preserved Nakayama', 0, '[]', '[]', '[]', '[]');
                """);
            Assert.IsFalse((await previous.Database.GetAppliedMigrationsAsync()).Contains(newMigration));

            await previous.Database.MigrateAsync();
            var appliedAfterUpgrade = (await previous.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.AreEqual(1, appliedAfterUpgrade.Count(x => x == newMigration));
            Assert.AreEqual(1, await previous.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'index' AND name = 'IX_RacePredictionContexts_RaceDate_RaceNumber'")
                .SingleAsync());
            Assert.AreEqual(2, await previous.RacePredictionContexts.CountAsync());
            var tokyo = await previous.RacePredictionContexts.SingleAsync(x => x.RaceId == "race-upgrade-tokyo");
            Assert.AreEqual("Preserved Tokyo", tokyo.RaceName);
            Assert.AreEqual(new DateOnly(2037, 9, 27), tokyo.RaceDate);
            Assert.AreEqual("TOKYO", tokyo.RacecourseCode);
            Assert.AreEqual(11, tokyo.RaceNumber);
            var nakayama = await previous.RacePredictionContexts.SingleAsync(x => x.RaceId == "race-upgrade-nakayama");
            Assert.AreEqual("Preserved Nakayama", nakayama.RaceName);
            Assert.AreEqual(new DateOnly(2037, 9, 27), nakayama.RaceDate);
            Assert.AreEqual("NAKAYAMA", nakayama.RacecourseCode);
            Assert.AreEqual(11, nakayama.RaceNumber);

            await previous.Database.MigrateAsync();
            var appliedAfterRepeat = (await previous.Database.GetAppliedMigrationsAsync()).ToList();
            CollectionAssert.AreEqual(appliedAfterUpgrade, appliedAfterRepeat,
                "Reapplying migrations must not add duplicate history rows or alter the upgraded schema.");
            Assert.AreEqual(1, await previous.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'index' AND name = 'IX_RacePredictionContexts_RaceDate_RaceNumber'")
                .SingleAsync());
        }
    }

    [TestMethod]
    public async Task Migrator_BaselinesExistingEnsureCreatedDatabase()
    {
        using var provider = new SqliteDbContextProvider();
        await using (var legacyContext = provider.CreateContext())
        {
            await legacyContext.Database.EnsureCreatedAsync();
            legacyContext.OwnerAliasMappings.Add(new OwnerAliasMappingReadModel
            {
                NormalizedAlias = "legacy-time",
                AliasName = "legacy-time",
                OwnerId = "owner-1",
                ActorId = "test",
                Reason = "test",
                CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 30, 0, TimeSpan.FromHours(9))
            });
            await legacyContext.SaveChangesAsync();
            await legacyContext.Database.ExecuteSqlRawAsync(
                "UPDATE OwnerAliasMappings SET CreatedAt = '2026-09-13T15:30:00.0000000+00:00' WHERE NormalizedAlias = 'legacy-time'");
        }

        var migrator = CreateMigrator(provider);
        await migrator.MigrateAsync();

        await using var context = provider.CreateContext();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        CollectionAssert.AreEquivalent(context.Database.GetMigrations().ToList(), applied);
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddRaceReacquisitionMetadata", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_InitialEventStore", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddOwnerAliasAdministration", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddOwnerDisplayName", StringComparison.Ordinal)));
        var migrated = await context.OwnerAliasMappings.SingleAsync(x => x.NormalizedAlias == "legacy-time");
        Assert.AreEqual(new DateTimeOffset(2026, 9, 14, 0, 30, 0, TimeSpan.FromHours(9)), migrated.CreatedAt);
    }

    [TestMethod]
    public async Task Migrator_UpgradesPreviousEnsureCreatedSchemaWithoutLosingData()
    {
        using var provider = new SqliteDbContextProvider();
        await using (var previous = provider.CreateContext())
        {
            await previous.Database.EnsureCreatedAsync();
            await previous.Database.ExecuteSqlRawAsync("DROP TABLE SubjectIdentificationRepairIssues");
            await previous.Database.ExecuteSqlRawAsync("DROP TABLE HorseIdentityRepairCandidates");
            await previous.Database.ExecuteSqlRawAsync("DROP TABLE HorseIdentityRepairRedirects");
            await previous.Database.ExecuteSqlRawAsync("DROP TABLE JraSubjectProfileReadModel");
            await previous.Database.ExecuteSqlRawAsync("INSERT INTO Horses (HorseId, RegisteredName, NormalizedName, Aliases) VALUES ('horse-legacy', 'preserved', 'preserved', '[]')");
        }
        await CreateMigrator(provider).MigrateAsync();
        await using var context = provider.CreateContext();
        Assert.AreEqual("preserved", await context.Database.SqlQueryRaw<string>("SELECT RegisteredName AS Value FROM Horses").SingleAsync());
        Assert.AreEqual(0, await context.Set<HorseRacingPrediction.Application.Queries.ReadModels.JraSubjectProfileReadModel>().CountAsync());
        CollectionAssert.AreEquivalent(context.Database.GetMigrations().ToList(), (await context.Database.GetAppliedMigrationsAsync()).ToList());
    }

    [TestMethod]
    public async Task Migrator_UpgradesJraProfileEnsureCreatedSchemaWithIdentityRepairTables()
    {
        using var provider = new SqliteDbContextProvider();
        await using (var previous = provider.CreateContext())
        {
            await previous.Database.EnsureCreatedAsync();
            await previous.Database.ExecuteSqlRawAsync("DROP TABLE SubjectIdentificationRepairIssues");
            await previous.Database.ExecuteSqlRawAsync("DROP TABLE HorseIdentityRepairCandidates");
            await previous.Database.ExecuteSqlRawAsync("DROP TABLE HorseIdentityRepairRedirects");
            await previous.Database.ExecuteSqlRawAsync("INSERT INTO Horses (HorseId, RegisteredName, NormalizedName, Aliases) VALUES ('horse-legacy', 'preserved', 'preserved', '[]')");
        }

        await CreateMigrator(provider).MigrateAsync();

        await using var context = provider.CreateContext();
        Assert.AreEqual("preserved", await context.Database.SqlQueryRaw<string>("SELECT RegisteredName AS Value FROM Horses").SingleAsync());
        Assert.AreEqual(0, await context.HorseIdentityRepairCandidates.CountAsync());
        Assert.AreEqual(0, await context.HorseIdentityRepairRedirects.CountAsync());
        CollectionAssert.AreEquivalent(context.Database.GetMigrations().ToList(), (await context.Database.GetAppliedMigrationsAsync()).ToList());
    }

    [TestMethod]
    public async Task DateTimeOffsetProperty_IsStoredWithoutTimezoneAndMaterializedAsJst()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<EventStoreDbContext>().UseSqlite(connection).Options;
        await using var context = new EventStoreDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.OwnerAliasMappings.Add(new OwnerAliasMappingReadModel
        {
            NormalizedAlias = "test",
            AliasName = "test",
            OwnerId = "owner-1",
            ActorId = "test",
            Reason = "test",
            CreatedAt = new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.Zero)
        });
        await context.SaveChangesAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CreatedAt FROM OwnerAliasMappings WHERE NormalizedAlias='test'";
        var stored = Convert.ToString(await command.ExecuteScalarAsync());
        context.ChangeTracker.Clear();
        var materialized = (await context.OwnerAliasMappings.SingleAsync()).CreatedAt;

        Assert.AreEqual("2026-09-14 00:30:00", stored);
        Assert.AreEqual(TimeSpan.FromHours(9), materialized.Offset);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 14, 0, 30, 0, TimeSpan.FromHours(9)), materialized);
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        var provider = new SqliteDbContextProvider();
        provider.Dispose();
    }

    private static SqliteDatabaseMigrator CreateMigrator(SqliteDbContextProvider provider)
    {
        return new SqliteDatabaseMigrator(
            provider,
            Options.Create(new SqliteMigrationOptions { BackupBeforeMigration = false }),
            NullLogger<SqliteDatabaseMigrator>.Instance);
    }
}
