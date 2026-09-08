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
        var connectionStringBuilder = new SqliteConnectionStringBuilder(connectionString)
        {
            Pooling = false
        };
        if (connectionStringBuilder.DataSource == ":memory:")
        {
            connectionStringBuilder.DataSource = "hrp-" + Guid.NewGuid().ToString("N");
            connectionStringBuilder.Mode = SqliteOpenMode.Memory;
            connectionStringBuilder.Cache = SqliteCacheMode.Shared;
        }
        // インメモリDBの寿命を維持する接続。各DbContextは専用接続を所有する。
        _connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
        _connection.Open();

        _options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite(connectionStringBuilder.ConnectionString)
            .Options;

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
