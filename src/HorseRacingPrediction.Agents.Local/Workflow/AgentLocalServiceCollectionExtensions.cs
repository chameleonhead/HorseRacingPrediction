using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Agents.Local.Workflow;

public static class AgentLocalServiceCollectionExtensions
{
    public static IServiceCollection AddHorseRacingAgentDomainSupport(this IServiceCollection services, string connectionString)
    {
        services.AddSqliteDbContextProvider(connectionString);

        services.AddSingleton<HorseWeightHistoryLocator>();
        services.AddSingleton<PredictionComparisonViewLocator>();
        services.AddSingleton<MemoBySubjectLocator>();
        services.AddSingleton<HorseRaceHistoryLocator>();
        services.AddSingleton<JockeyRaceHistoryLocator>();

        services.AddEventFlow(options =>
        {
            options
                .AddDefaults(typeof(RaceAggregate).Assembly)
                .AddDefaults(typeof(CreateRaceCommand).Assembly)
                .UseEntityFrameworkSqliteEventStore(connectionString)
                .UseEntityFrameworkReadModel<RaceSummaryReadModel, EventStoreDbContext>()
                .UseEntityFrameworkReadModel<HorseReadModel, EventStoreDbContext>()
                .UseEntityFrameworkReadModel<JockeyReadModel, EventStoreDbContext>()
                .UseEntityFrameworkReadModel<TrainerReadModel, EventStoreDbContext>()
                .UseEntityFrameworkReadModel<RacePredictionContextReadModel, EventStoreDbContext>()
                .UseEntityFrameworkReadModel<RaceResultViewReadModel, EventStoreDbContext>()
                .UseEntityFrameworkReadModel<PredictionTicketReadModel, EventStoreDbContext>()
                .UseEntityFrameworkReadModel<HorseWeightHistoryReadModel, EventStoreDbContext, HorseWeightHistoryLocator>()
                .UseEntityFrameworkReadModel<PredictionComparisonViewReadModel, EventStoreDbContext, PredictionComparisonViewLocator>()
                .UseEntityFrameworkReadModel<MemoBySubjectReadModel, EventStoreDbContext, MemoBySubjectLocator>()
                .UseEntityFrameworkReadModel<HorseRaceHistoryReadModel, EventStoreDbContext, HorseRaceHistoryLocator>()
                .UseEntityFrameworkReadModel<JockeyRaceHistoryReadModel, EventStoreDbContext, JockeyRaceHistoryLocator>();
        });

        services.AddTransient<IRaceQueryService, EventFlowRaceQueryService>();
        services.AddTransient<IPredictionWriteService, EventFlowPredictionWriteService>();
        services.AddTransient<IDataCollectionWriteService, EventFlowDataCollectionWriteService>();
        services.AddTransient<RaceQueryTools>();
        services.AddTransient<PredictionWriteTools>();
        services.AddTransient<DataCollectionWriteTools>();
        return services;
    }
}