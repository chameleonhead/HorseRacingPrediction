using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common.Time;
using Amazon.CloudWatch;
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
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.MachineLearning;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.OpenApi;
using Microsoft.Extensions.Options;
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

var dataProtectionKeysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysDirectory))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory));
}

builder.Services.AddSingleton<ApiKeyEndpointFilter>();
builder.Services.AddSingleton<RaceActiveCollectionEndpointFilter>();
builder.Services.AddSingleton<RaceWriteCoordinator>();
builder.Services.AddSingleton<RaceWriteEndpointFilter>();
builder.Services.AddSingleton<RacePredictionReadEndpointFilter>();
builder.Services.AddTransient<RaceEntryRepairInspector>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JstDateTimeOffsetJsonConverter()));

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
var sqlite = new SqliteConnectionStringBuilder(connectionString);
if (!string.IsNullOrWhiteSpace(sqlite.DataSource) && sqlite.DataSource != ":memory:" && !Path.IsPathRooted(sqlite.DataSource))
{
    sqlite.DataSource = Path.GetFullPath(sqlite.DataSource, builder.Environment.ContentRootPath);
    connectionString = sqlite.ConnectionString;
}

builder.Services.AddSqliteDbContextProvider(connectionString, builder.Configuration);

builder.Services.AddSingleton<HorseWeightHistoryLocator>();
builder.Services.AddSingleton<PredictionComparisonViewLocator>();
builder.Services.AddSingleton<MemoBySubjectLocator>();
builder.Services.AddSingleton<HorseRaceHistoryLocator>();
builder.Services.AddSingleton<JockeyRaceHistoryLocator>();
builder.Services.AddSingleton<JraSubjectProfileLocator>();
builder.Services.AddRacePredictor();
builder.Services.Configure<PredictionScheduleOptions>(builder.Configuration.GetSection(PredictionScheduleOptions.SectionName));
builder.Services.AddSingleton<PredictionScheduleStore>();
builder.Services.AddSingleton<IPredictionSchedule>(services => services.GetRequiredService<PredictionScheduleStore>());
builder.Services.Configure<CollectionPlatformOptions>(builder.Configuration.GetSection(CollectionPlatformOptions.SectionName));
builder.Services.PostConfigure<CollectionPlatformOptions>(options =>
{
    var configured = string.IsNullOrWhiteSpace(options.StateDirectory) ? "collection-platform-state" : options.StateDirectory;
    if (!Path.IsPathRooted(configured)) options.StateDirectory = Path.GetFullPath(configured, builder.Environment.ContentRootPath);
});
builder.Services.AddSingleton<IRaceResourceIdentityResolver, HorseRacingPrediction.Api.DomainRaceResourceIdentityResolver>();
builder.Services.AddSingleton<IAmazonCloudWatch>(_ => new AmazonCloudWatchClient());
builder.Services.AddSingleton<ICollectionDispatchMetricPublisher, CloudWatchCollectionDispatchMetricPublisher>();
builder.Services.Configure<CollectionDispatchMetricQueueOptions>(builder.Configuration.GetSection("CollectionDispatchTelemetryQueue"));
builder.Services.AddSingleton<CollectionDispatchMetricQueue>(services => new CollectionDispatchMetricQueue(
    services.GetRequiredService<ICollectionDispatchMetricPublisher>(),
    services.GetRequiredService<IOptions<CollectionDispatchMetricQueueOptions>>(),
    services.GetRequiredService<ILogger<CollectionDispatchMetricQueue>>(),
    services.GetRequiredService<CollectionRuntimeStatusRecorder>()));
builder.Services.AddSingleton<ICollectionDispatchMetricQueue>(services =>
    services.GetRequiredService<CollectionDispatchMetricQueue>());
builder.Services.AddSingleton<CollectionDispatchTelemetry>();
builder.Services.AddSingleton<ICollectionDispatchTelemetry>(services =>
    services.GetRequiredService<CollectionDispatchTelemetry>());
builder.Services.AddSingleton<CollectionPlatformStore>();
builder.Services.Configure<CollectionMonitoringOptions>(
    builder.Configuration.GetSection(CollectionMonitoringOptions.SectionName));
builder.Services.AddSingleton<CollectionMonitoringService>();
builder.Services.AddSingleton<ICollectionSchedulePolicy, JraCollectionSchedulePolicy>();
builder.Services.AddSingleton<INamedRevisionImpactCondition, HorseProfileLegacyLayoutRevisionCondition>();
builder.Services.AddSingleton<INamedRevisionImpactCondition, RaceResultDeadHeatBeforeRevisionFiveCondition>();
builder.Services.AddSingleton<CollectionQueueCircuitBreakerState>();
var collectionQueueSection = builder.Configuration.GetSection(CollectionQueueOptions.SectionName);
builder.Services.Configure<CollectionQueueOptions>(collectionQueueSection);
var maintenanceMode = builder.Configuration.GetValue($"{CollectionPlatformOptions.SectionName}:MaintenanceMode", false);
builder.Services.PostConfigure<CollectionQueueOptions>(options =>
{
    if (maintenanceMode) options.Enabled = false;
});
builder.Services.Configure<CollectionJobWatchdogOptions>(
    builder.Configuration.GetSection(CollectionJobWatchdogOptions.SectionName));
var backgroundSchedulersEnabled = builder.Configuration.GetValue("CollectionOrchestration:BackgroundSchedulersEnabled", true);
var watchdogEnabled = builder.Configuration.GetValue($"{CollectionJobWatchdogOptions.SectionName}:Enabled", true);
var watchdogIntervalMinutes = builder.Configuration.GetValue($"{CollectionJobWatchdogOptions.SectionName}:IntervalMinutes", 5);
var queueEnabled = !maintenanceMode && collectionQueueSection.GetValue<bool>(nameof(CollectionQueueOptions.Enabled));
var localQueueEnabled = string.Equals(collectionQueueSection[nameof(CollectionQueueOptions.Provider)] ?? "Sqs",
    "Local", StringComparison.OrdinalIgnoreCase);
var deadLetterSection = builder.Configuration.GetSection(CollectionDeadLetterQueueReconcilerOptions.SectionName);
builder.Services.AddCollectionBackgroundSchedulers(
    backgroundSchedulersEnabled,
    watchdogEnabled,
    watchdogIntervalMinutes,
    queueEnabled,
    collectionQueueSection.GetValue(nameof(CollectionQueueOptions.DispatchIntervalSeconds), 1),
    deadLetterReconcilerEnabled: deadLetterSection.GetValue(nameof(CollectionDeadLetterQueueReconcilerOptions.Enabled), true),
    deadLetterIntervalSeconds: deadLetterSection.GetValue(nameof(CollectionDeadLetterQueueReconcilerOptions.IntervalSeconds), 30),
    metricDeliveryEnabled: queueEnabled && !localQueueEnabled,
    alertsEnabled: true,
    maintenanceMode: maintenanceMode);
builder.Services.Configure<CollectionDeadLetterQueueReconcilerOptions>(
    deadLetterSection);
if (queueEnabled)
{
    if (string.Equals(collectionQueueSection[nameof(CollectionQueueOptions.Provider)], "Local", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton(_ => new LocalCollectionQueue(
            collectionQueueSection[nameof(CollectionQueueOptions.LocalDatabasePath)]
            ?? "collection-platform-state/local-collection-queue.db"));
        builder.Services.AddSingleton<LocalCollectionTaskQueue>();
        builder.Services.AddSingleton<ICollectionTaskQueue>(services => services.GetRequiredService<LocalCollectionTaskQueue>());
        builder.Services.AddSingleton<ICollectionPlatformTaskQueue>(services => services.GetRequiredService<LocalCollectionTaskQueue>());
    }
    else
    {
        builder.Services.AddHostedService(services => services.GetRequiredService<CollectionDispatchMetricQueue>());
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
        builder.Services.AddSingleton<ICollectionPlatformTaskQueue>(services =>
            (SqsCollectionTaskQueue)services.GetRequiredService<ICollectionTaskQueue>());
    }
    builder.Services.AddSingleton<CollectionPlatformOutboxDispatcher>(services => new CollectionPlatformOutboxDispatcher(
        services.GetRequiredService<CollectionPlatformStore>(),
        services.GetRequiredService<ICollectionPlatformTaskQueue>(),
        services.GetRequiredService<IOptions<CollectionQueueOptions>>(),
        services.GetRequiredService<ILogger<CollectionPlatformOutboxDispatcher>>(),
        services.GetRequiredService<ICollectionDispatchTelemetry>(),
        services.GetRequiredService<CollectionRuntimeStatusRecorder>()));
    builder.Services.AddHostedService(services => services.GetRequiredService<CollectionPlatformOutboxDispatcher>());
    builder.Services.AddHostedService<CollectionPlatformDeadLetterReconciler>(services =>
        new CollectionPlatformDeadLetterReconciler(
            services.GetRequiredService<CollectionPlatformStore>(),
            services.GetRequiredService<ICollectionPlatformTaskQueue>(),
            services.GetRequiredService<IOptions<CollectionDeadLetterQueueReconcilerOptions>>(),
            services.GetRequiredService<ILogger<CollectionPlatformDeadLetterReconciler>>(),
            services.GetRequiredService<CollectionRuntimeStatusRecorder>()));
}
else
{
    builder.Services.AddSingleton<NullCollectionTaskQueue>();
    builder.Services.AddSingleton<ICollectionTaskQueue>(services => services.GetRequiredService<NullCollectionTaskQueue>());
    builder.Services.AddSingleton<ICollectionPlatformTaskQueue>(services => services.GetRequiredService<NullCollectionTaskQueue>());
}
var jobFailureNotificationSection = builder.Configuration.GetSection(JobFailureNotificationOptions.SectionName);
builder.Services.Configure<JobFailureNotificationOptions>(jobFailureNotificationSection);
// SNSクライアントとPublisherは通常モードでは常に登録する。JobFailureNotifications:Enabledは
// 既存契約どおりPublisherの送信可否を制御しない。MaintenanceModeだけは自動配送ホストを登録しない。
builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ => new AmazonSimpleNotificationServiceClient());
builder.Services.AddSingleton<ICollectionPipelineAlertPublisher, SnsCollectionPipelineAlertPublisher>();
if (!maintenanceMode)
{
    builder.Services.AddHostedService<CollectionPipelineAlertDispatchService>(services =>
        new CollectionPipelineAlertDispatchService(
            services.GetRequiredService<CollectionPlatformStore>(),
            services.GetRequiredService<ICollectionPipelineAlertPublisher>(),
            services.GetRequiredService<ILogger<CollectionPipelineAlertDispatchService>>(),
            services.GetRequiredService<CollectionRuntimeStatusRecorder>()));
}

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
    .UseEntityFrameworkReadModel<JraSubjectProfileReadModel, EventStoreDbContext, JraSubjectProfileLocator>()
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

var collectionPlatform = app.Services.GetRequiredService<CollectionPlatformStore>();
await collectionPlatform.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race, 1, "Initial", false);
await collectionPlatform.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race, HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
    "Race resource artifact state machine", false);
await collectionPlatform.RegisterDefinitionAsync(new("race-odds"), "Race odds", CollectionResourceType.RaceOdds, 1, "Initial", false);
await SubjectCollectionDefinitions.RegisterAsync(collectionPlatform);

await app.Services.GetRequiredService<SqliteDatabaseMigrator>().MigrateAsync();
if (maintenanceMode)
{
    app.Logger.LogInformation("CollectionPlatform:MaintenanceMode is enabled; automatic collection startup actions are suspended.");
}
else
{
    var subjectRecovery = await SubjectIdentificationAutoRecovery.RunOnceAsync(collectionPlatform, app.Logger);
    app.Logger.LogInformation(
        "Subject identification recovery examined {Examined}, created {Recovered}, reused {Reused}, suppressed {Suppressed}, skipped {Skipped}, failed {Failed}.",
        subjectRecovery.Examined, subjectRecovery.Recovered, subjectRecovery.Reused,
        subjectRecovery.Suppressed, subjectRecovery.Skipped, subjectRecovery.Failed);
}
// キューが有効な場合は、起動後にキュー深度を記録して dispatcher を一度実行する。
// Watchdog は独立した設定のホストサービスであり、この起動時キックでは呼び出さない。
// MaintenanceMode では queueEnabled が false となり、このキックを含む自動処理を停止する。
if (queueEnabled)
{
    _ = Task.Run(async () =>
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        try
        {
            var queue = app.Services.GetRequiredService<ICollectionTaskQueue>();
            var depth = await queue.GetQueueDepthAsync(CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation(
                "起動直後のSQSキュー調査: 可視メッセージ={Visible} 処理中メッセージ={NotVisible}",
                depth.VisibleCount,
                depth.NotVisibleCount);

            await app.Services.GetRequiredService<CollectionPlatformOutboxDispatcher>()
                .DispatchOnceAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "起動直後のジョブ実行確認でエラーが発生しました。");
        }
    });
}

app.UseForwardedHeaders();
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseApiKeyProtection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapApiEndpoints();
app.MapAdminEndpoints();
app.MapCollectionApiV2Endpoints();

app.Run();
