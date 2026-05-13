using EventFlow.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Application.Tests;

internal sealed class TestSqliteDbContextProvider : IDbContextProvider<TestEventStoreDbContext>, IDisposable
{
    private readonly DbContextOptions<TestEventStoreDbContext> _options;
    private readonly SqliteConnection _connection;

    public TestSqliteDbContextProvider(string connectionString = "DataSource=:memory:")
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        _options = new DbContextOptionsBuilder<TestEventStoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new TestEventStoreDbContext(_options);
        context.Database.EnsureCreated();
    }

    public TestEventStoreDbContext CreateContext()
    {
        return new TestEventStoreDbContext(_options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}