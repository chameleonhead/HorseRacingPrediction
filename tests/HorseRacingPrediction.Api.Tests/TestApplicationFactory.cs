using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.MachineLearning;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.FluentUI.AspNetCore.Components;
using HorseRacingPrediction.Api.CollectionController;

namespace HorseRacingPrediction.Api.Tests;

internal static class TestApplicationFactory
{
    public const string TestApiKey = "test-api-key-12345";

    public static async Task<(WebApplication App, HttpClient Client)> CreateAsync(
        string connectionString = "DataSource=:memory:")
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

        builder.Services.Configure<ApiKeyOptions>(opts =>
        {
            opts.HeaderName = "X-Api-Key";
            opts.Key = TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        builder.Services.AddAdminAuthentication();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddFluentUIComponents();
        builder.Services.AddScoped<IDialogService, DialogService>();
        builder.Services.AddScoped<IToastService, ToastService>();

        builder.Services.AddSingleton(_ =>
        {
            var provider = new SqliteDbContextProvider(connectionString);
            using var context = provider.CreateContext();
            context.Database.EnsureCreated();
            return provider;
        });
        builder.Services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(
            services => services.GetRequiredService<SqliteDbContextProvider>());

        builder.Services.AddSingleton<HorseWeightHistoryLocator>();
        builder.Services.AddSingleton<PredictionComparisonViewLocator>();
        builder.Services.AddSingleton<MemoBySubjectLocator>();
        builder.Services.AddSingleton<HorseRaceHistoryLocator>();
        builder.Services.AddSingleton<JockeyRaceHistoryLocator>();
        builder.Services.AddRacePredictor();
        builder.Services.Configure<AgentProcessingOptions>(options =>
        {
            options.StateDirectory = Path.Combine(Path.GetTempPath(), "hrp-api-tests", Guid.NewGuid().ToString("N"));
            options.JobStoreFileName = "collection-tasks.db";
        });
        builder.Services.AddSingleton<ProcessingStateStore>();
        builder.Services.AddSingleton<IProcessingStateStore>(services => services.GetRequiredService<ProcessingStateStore>());
        builder.Services.AddSingleton<CollectionExecutionTrigger>();
        builder.Services.AddSingleton<CollectionMaintenanceState>();
        builder.Services.AddSingleton<ICollectionTaskQueue, NullCollectionTaskQueue>();

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
        app.Use(async (context, next) =>
        {
            var maintenance = context.RequestServices.GetRequiredService<CollectionMaintenanceState>();
            var isMutation = !HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method)
                && !HttpMethods.IsOptions(context.Request.Method);
            if (maintenance.IsActive && isMutation
                && !context.Request.Path.StartsWithSegments("/api/collection/reset")
                && !context.Request.Path.Equals("/api/admin/jobs/resume", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { message = "収集データベースのメンテナンス中です。" });
                return;
            }

            await next();
        });
        app.UseAntiforgery();
        app.MapApiEndpoints();
        app.MapAdminEndpoints();
        app.MapProcessingStateRpcEndpoint();
        app.MapAgentDashboardEndpoints();
        app.MapJobManagementEndpoints();
        app.MapAgentAcquisitionStatusEndpoints();

        await app.StartAsync();
        var client = app.GetTestClient();
        return (app, client);
    }
}
