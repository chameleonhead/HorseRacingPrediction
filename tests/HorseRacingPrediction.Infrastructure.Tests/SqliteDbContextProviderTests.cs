using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
    public void Constructor_CreatesSchemaAutomatically()
    {
        using var provider = new SqliteDbContextProvider();
        using var context = provider.CreateContext();

        var tableNames = context.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .ToList();

        Assert.IsTrue(tableNames.Contains("EventEntity"));
        Assert.IsTrue(tableNames.Contains("SnapshotEntity"));
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        var provider = new SqliteDbContextProvider();
        provider.Dispose();
    }
}
