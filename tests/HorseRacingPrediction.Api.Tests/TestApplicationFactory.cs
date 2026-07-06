using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.MachineLearning;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

internal static class TestApplicationFactory
{
    public const string TestApiKey = "test-api-key-12345";

    public static async Task<(WebApplication App, HttpClient Client)> CreateAsync(
        string connectionString = "DataSource=:memory:")
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();

        builder.Services.Configure<ApiKeyOptions>(opts =>
        {
            opts.HeaderName = "X-Api-Key";
            opts.Key = TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        builder.Services.AddAdminAuthentication();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var dbContextProvider = new SqliteDbContextProvider(connectionString);
        builder.Services.AddSingleton(dbContextProvider);
        builder.Services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(dbContextProvider);

        builder.Services.AddSingleton<HorseWeightHistoryLocator>();
        builder.Services.AddSingleton<PredictionComparisonViewLocator>();
        builder.Services.AddSingleton<MemoBySubjectLocator>();
        builder.Services.AddSingleton<HorseRaceHistoryLocator>();
        builder.Services.AddSingleton<JockeyRaceHistoryLocator>();
        builder.Services.AddRacePredictor();

        builder.Services.AddEventFlow(options =>
        {
            options
                .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
                .AddDefaults(typeof(RaceAggregate).Assembly)
                .AddDefaults(typeof(CreateRaceCommand).Assembly)
                .UseEntityFrameworkEventStore<EventStoreDbContext>()
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

        var app = builder.Build();
        app.UseApiKeyProtection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapApiEndpoints();
        app.MapAdminEndpoints();

        await app.StartAsync();
        var client = app.GetTestClient();
        return (app, client);
    }
}
