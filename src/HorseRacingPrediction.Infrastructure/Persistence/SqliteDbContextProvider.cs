using EventFlow.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Infrastructure.Persistence;

public class SqliteDbContextProvider : IDbContextProvider<EventStoreDbContext>, IDisposable
{
    private readonly DbContextOptions<EventStoreDbContext> _options;
    private readonly SqliteConnection _connection;

    public SqliteDbContextProvider(string connectionString = "DataSource=:memory:")
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        _options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new EventStoreDbContext(_options);
        context.Database.EnsureCreated();
    }

    public EventStoreDbContext CreateContext()
    {
        return new EventStoreDbContext(_options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
