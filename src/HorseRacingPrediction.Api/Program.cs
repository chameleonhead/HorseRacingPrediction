using Amazon.SimpleNotificationService;
using Amazon.SQS;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Api;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.MachineLearning;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.OpenApi;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiKeyOptions>(options =>
{
    options.HeaderName = builder.Configuration["ApiKey:HeaderName"] ?? "X-Api-Key";
    var configuredKey = builder.Configuration["ApiKey:Key"];
    options.Key = string.IsNullOrWhiteSpace(configuredKey)
        ? Environment.GetEnvironmentVariable("HORSE_RACING_API_KEY")
        : configuredKey;
});

builder.Services.AddSingleton<ApiKeyEndpointFilter>();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddAdminAuthentication();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();
builder.Services.AddScoped<IDialogService, DialogService>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddSingleton<AdminApiBaseAddressResolver>();
builder.Services.AddHttpClient<AdminApiClient>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1;

    foreach (var configuredProxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(configuredProxy, out var proxyAddress))
            options.KnownProxies.Add(proxyAddress);
    }

    foreach (var configuredNetwork in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = configuredNetwork.Split('/', 2);
        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var networkAddress)
            && int.TryParse(parts[1], out var prefixLength))
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(networkAddress, prefixLength));
        }
    }
});
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace("+", "."));
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API キーをヘッダーに指定してください"
    });
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("ApiKey"),
            new List<string>()
        }
    });
});

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Data Source=eventstore.db";

builder.Services.AddSqliteDbContextProvider(connectionString, builder.Configuration);

builder.Services.AddSingleton<HorseWeightHistoryLocator>();
builder.Services.AddSingleton<PredictionComparisonViewLocator>();
builder.Services.AddSingleton<MemoBySubjectLocator>();
builder.Services.AddSingleton<HorseRaceHistoryLocator>();
builder.Services.AddSingleton<JockeyRaceHistoryLocator>();
builder.Services.AddRacePredictor();
builder.Services.Configure<AgentProcessingOptions>(builder.Configuration.GetSection("CollectionProcessing"));
builder.Services.AddSingleton<ProcessingStateStore>();
builder.Services.AddSingleton<IProcessingStateStore>(services => services.GetRequiredService<ProcessingStateStore>());
builder.Services.AddSingleton<CollectionExecutionTrigger>();
builder.Services.AddSingleton<CollectionMaintenanceState>();
builder.Services.AddSingleton<CollectionQueueCircuitBreakerState>();
var collectionQueueSection = builder.Configuration.GetSection(CollectionQueueOptions.SectionName);
builder.Services.Configure<CollectionQueueOptions>(collectionQueueSection);
if (collectionQueueSection.GetValue<bool>(nameof(CollectionQueueOptions.Enabled)))
{
    builder.Services.AddSingleton<IAmazonSQS>(_ =>
    {
        var serviceUrl = collectionQueueSection[nameof(CollectionQueueOptions.ServiceUrl)];
        if (string.IsNullOrWhiteSpace(serviceUrl)) return new AmazonSQSClient();

        return new AmazonSQSClient(new AmazonSQSConfig
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = builder.Configuration["AWS_REGION"] ?? "ap-northeast-1"
        });
    });
    builder.Services.AddSingleton<ICollectionTaskQueue, SqsCollectionTaskQueue>();
    builder.Services.AddHostedService<CollectionTaskOutboxDispatcher>();
    builder.Services.Configure<CollectionDeadLetterQueueReconcilerOptions>(
        builder.Configuration.GetSection(CollectionDeadLetterQueueReconcilerOptions.SectionName));
    builder.Services.AddHostedService<CollectionDeadLetterQueueReconciler>();
}
else
{
    builder.Services.AddSingleton<ICollectionTaskQueue, NullCollectionTaskQueue>();
}
builder.Services.AddSingleton<CollectionResetCoordinator>();
var jobFailureNotificationSection = builder.Configuration.GetSection(JobFailureNotificationOptions.SectionName);
builder.Services.Configure<JobFailureNotificationOptions>(jobFailureNotificationSection);
// SNSクライアント自体とCollectionPipelineAlertPublisher（収集ジョブ全体停止アラート）は、
// JobFailureNotifications:Enabled のON/OFFに関わらず常に登録する。SNSサブスクリプションは
// 運用側で作成済みの前提で、アプリ側の設定トグルで送信有無を左右させないため。
builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ => new AmazonSimpleNotificationServiceClient());
builder.Services.AddSingleton<ICollectionPipelineAlertPublisher, SnsCollectionPipelineAlertPublisher>();
if (jobFailureNotificationSection.GetValue<bool>(nameof(JobFailureNotificationOptions.Enabled)))
{
    builder.Services.AddSingleton<IJobFailureNotificationPublisher, SnsJobFailureNotificationPublisher>();
    builder.Services.AddHostedService<JobFailureNotificationDispatcher>();
}
builder.Services.AddHostedService<CollectionPlanningScheduler>();
builder.Services.Configure<CollectionJobWatchdogOptions>(
    builder.Configuration.GetSection(CollectionJobWatchdogOptions.SectionName));
builder.Services.AddHostedService<CollectionJobWatchdogService>();

builder.Services.AddEventFlow(options =>
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

var app = builder.Build();

await app.Services.GetRequiredService<SqliteDatabaseMigrator>().MigrateAsync();
app.Services.GetRequiredService<CollectionResetCoordinator>().ResumeIfNeeded();

app.UseForwardedHeaders();
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseStaticFiles();
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
app.MapAgentDashboardEndpoints();
app.MapCollectionResetEndpoints();
app.MapJobManagementEndpoints();
app.MapAgentAcquisitionStatusEndpoints();
app.MapProcessingStateRpcEndpoint();

app.Run();
