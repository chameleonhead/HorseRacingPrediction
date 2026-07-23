using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HorseRacingPrediction.Infrastructure.Persistence;

public sealed class EventStoreDbContextFactory : IDesignTimeDbContextFactory<EventStoreDbContext>
{
    public EventStoreDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HORSE_RACING_MIGRATION_CONNECTION")
            ?? "Data Source=eventstore-design.db";

        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new EventStoreDbContext(options);
    }
}
