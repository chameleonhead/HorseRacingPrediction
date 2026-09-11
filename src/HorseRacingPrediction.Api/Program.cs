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
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Data.Sqlite;
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

var dataProtectionKeysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysDirectory))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory));
}

builder.Services.AddSingleton<ApiKeyEndpointFilter>();
builder.Services.AddSingleton<RaceActiveCollectionEndpointFilter>();
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
builder.Services.AddSingleton<CollectionPlatformStore>();
builder.Services.AddSingleton<ICollectionSchedulePolicy, JraCollectionSchedulePolicy>();
builder.Services.AddSingleton<INamedRevisionImpactCondition, HorseProfileLegacyLayoutRevisionCondition>();
builder.Services.AddSingleton<INamedRevisionImpactCondition, RaceResultDeadHeatBeforeRevisionFiveCondition>();
builder.Services.AddHostedService<CollectionScheduleService>();
builder.Services.AddHostedService<CollectionBackfillRecoveryService>();
builder.Services.AddSingleton<CollectionQueueCircuitBreakerState>();
var collectionQueueSection = builder.Configuration.GetSection(CollectionQueueOptions.SectionName);
builder.Services.Configure<CollectionQueueOptions>(collectionQueueSection);
builder.Services.Configure<CollectionJobWatchdogOptions>(
    builder.Configuration.GetSection(CollectionJobWatchdogOptions.SectionName));
builder.Services.Configure<CollectionDeadLetterQueueReconcilerOptions>(
    builder.Configuration.GetSection(CollectionDeadLetterQueueReconcilerOptions.SectionName));
builder.Services.AddHostedService<CollectionPlatformWatchdogService>();
if (collectionQueueSection.GetValue<bool>(nameof(CollectionQueueOptions.Enabled)))
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
    builder.Services.AddSingleton<CollectionPlatformOutboxDispatcher>();
    builder.Services.AddHostedService(services => services.GetRequiredService<CollectionPlatformOutboxDispatcher>());
    builder.Services.AddHostedService<CollectionPlatformDeadLetterReconciler>();
}
else
{
    builder.Services.AddSingleton<NullCollectionTaskQueue>();
    builder.Services.AddSingleton<ICollectionTaskQueue>(services => services.GetRequiredService<NullCollectionTaskQueue>());
    builder.Services.AddSingleton<ICollectionPlatformTaskQueue>(services => services.GetRequiredService<NullCollectionTaskQueue>());
}
var jobFailureNotificationSection = builder.Configuration.GetSection(JobFailureNotificationOptions.SectionName);
builder.Services.Configure<JobFailureNotificationOptions>(jobFailureNotificationSection);
// SNSクライアント自体とCollectionPipelineAlertPublisher（収集ジョブ全体停止アラート）は、
// JobFailureNotifications:Enabled のON/OFFに関わらず常に登録する。SNSサブスクリプションは
// 運用側で作成済みの前提で、アプリ側の設定トグルで送信有無を左右させないため。
builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ => new AmazonSimpleNotificationServiceClient());
builder.Services.AddSingleton<ICollectionPipelineAlertPublisher, SnsCollectionPipelineAlertPublisher>();
builder.Services.AddHostedService<CollectionPlanningScheduler>();

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
await collectionPlatform.RegisterDefinitionAsync(new("race-card"), "Race card", ResourceType.RaceCard, 1, "Initial", false);
await collectionPlatform.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race, 1, "Initial", false);
await collectionPlatform.RegisterDefinitionAsync(new("race-result"), "Race result", ResourceType.RaceResult, 1, "Initial", false);
await collectionPlatform.RegisterDefinitionAsync(new("race-odds"), "Race odds", ResourceType.RaceOdds, 1, "Initial", false);
await collectionPlatform.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse, 1, "Initial", false);
await collectionPlatform.RegisterDefinitionAsync(new("jockey-profile"), "Jockey profile", ResourceType.Jockey, 1, "Initial", false);
await collectionPlatform.RegisterDefinitionAsync(new("trainer-profile"), "Trainer profile", ResourceType.Trainer, 1, "Initial", false);

await app.Services.GetRequiredService<SqliteDatabaseMigrator>().MigrateAsync();
// 起動直後はホストサービス（Dispatcher/Watchdog）自体も初回サイクルを即時実行するが、
// 直前にクラッシュ復旧中の初期化（ResumeIfNeeded）がメンテナンス中の場合は、その完了を
// 待たずに終わってしまい、完了後に誰も再トリガーしないまま次の定期実行（最大数時間後）
// まで新規ジョブが投入されない空白が生じ得る。ここで初期化完了を待った上でSQSキューの
// 滞留状況を調査し、ディスパッチ・監視サイクルを明示的に1回実行することでその空白を埋める。
// CollectionTaskOutboxDispatcher / CollectionJobWatchdogService は CollectionQueue.Enabled
// が true の場合のみDIへ登録される（SQS未使用のローカル開発環境等では登録されない）。
// 以前はこのガードがなく、Enabled=false（既定値）のローカル実行で
// 「No service for type 'CollectionTaskOutboxDispatcher' has been registered.」という
// InvalidOperationExceptionがログに出続けていた（キャッチはされるためプロセスは落ちないが、
// 起動のたびに無意味なエラーログが発生していた）。
if (collectionQueueSection.GetValue<bool>(nameof(CollectionQueueOptions.Enabled)))
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
app.MapCollectionPlatformEndpoints();
app.MapRaceOddsEndpoints();
app.MapSubjectCollectionEndpoints();
app.MapPredictionScheduleEndpoints();

app.Run();
