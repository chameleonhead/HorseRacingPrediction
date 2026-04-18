using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IEventFlowOptions UseEntityFrameworkSqliteEventStore(
        this IEventFlowOptions options,
        string connectionString)
    {
        return options
            .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
            .UseEntityFrameworkEventStore<EventStoreDbContext>()
            .AddDbContextProvider<EventStoreDbContext, SqliteDbContextProvider>();
    }

    public static IServiceCollection AddSqliteDbContextProvider(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(new SqliteDbContextProvider(connectionString));
        services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(
            sp => sp.GetRequiredService<SqliteDbContextProvider>());
        return services;
    }
}
