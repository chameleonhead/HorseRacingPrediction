using EventFlow.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Infrastructure.Persistence;

public class EventStoreDbContext : DbContext
{
    public EventStoreDbContext(DbContextOptions<EventStoreDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddEventFlowEvents();
        modelBuilder.AddEventFlowSnapshots();
    }
}
