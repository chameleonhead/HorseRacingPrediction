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
    }

    [TestMethod]
    public async Task Migrator_CreatesSchemaAndMigrationHistory()
    {
        using var provider = new SqliteDbContextProvider();
        var migrator = CreateMigrator(provider);

        await migrator.MigrateAsync();

        await using var context = provider.CreateContext();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.IsTrue(applied.Single().EndsWith("_InitialEventStore", StringComparison.Ordinal));
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
        Assert.AreEqual(1, applied.Count);
        Assert.IsTrue(applied[0].EndsWith("_InitialEventStore", StringComparison.Ordinal));
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
