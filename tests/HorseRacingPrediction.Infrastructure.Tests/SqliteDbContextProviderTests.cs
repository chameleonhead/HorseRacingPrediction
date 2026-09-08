using HorseRacingPrediction.Infrastructure.Persistence;
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
    }

    [TestMethod]
    public async Task Migrator_BaselinesExistingEnsureCreatedDatabase()
    {
        using var provider = new SqliteDbContextProvider();
        await using (var legacyContext = provider.CreateContext())
            await legacyContext.Database.EnsureCreatedAsync();

        var migrator = CreateMigrator(provider);
        await migrator.MigrateAsync();

        await using var context = provider.CreateContext();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        CollectionAssert.AreEquivalent(context.Database.GetMigrations().ToList(), applied);
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddRaceReacquisitionMetadata", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_InitialEventStore", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddOwnerAliasAdministration", StringComparison.Ordinal)));
        Assert.IsTrue(applied.Any(x => x.EndsWith("_AddOwnerDisplayName", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Migrator_UpgradesPreviousEnsureCreatedSchemaWithoutLosingData()
    {
        using var provider = new SqliteDbContextProvider();
        await using (var previous = provider.CreateContext())
        {
            await previous.Database.EnsureCreatedAsync();
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
