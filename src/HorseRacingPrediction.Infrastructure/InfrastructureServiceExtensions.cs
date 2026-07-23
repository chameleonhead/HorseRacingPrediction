using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
            .UseEntityFrameworkEventStore<EventStoreDbContext>();
    }

    public static IServiceCollection AddSqliteDbContextProvider(
        this IServiceCollection services,
        string connectionString,
        IConfiguration? configuration = null)
    {
        services.AddSingleton(_ => new SqliteDbContextProvider(connectionString));
        services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(
            sp => sp.GetRequiredService<SqliteDbContextProvider>());
        if (configuration is not null)
            services.Configure<SqliteMigrationOptions>(configuration.GetSection("DatabaseMigration"));
        else
            services.Configure<SqliteMigrationOptions>(_ => { });
        services.AddSingleton<SqliteDatabaseMigrator>();
        return services;
    }
}
