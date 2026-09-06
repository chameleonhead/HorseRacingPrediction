using System.Linq;
using System.Text.Json;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Scraping.Jra;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
var runOnce = args.Contains("--once", StringComparer.OrdinalIgnoreCase);

builder.Services.Configure<ApiClientOptions>(
    builder.Configuration.GetSection(ApiClientOptions.SectionName));
builder.Services.AddSingleton<IValidateOptions<ApiClientOptions>, ApiClientOptionsValidator>();
builder.Services.AddOptions<ApiClientOptions>()
    .Bind(builder.Configuration.GetSection(ApiClientOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddHttpAgentServices();

builder.Services.Configure<AgentProcessingOptions>(
    builder.Configuration.GetSection(AgentProcessingOptions.SectionName));

builder.Services.AddJraScraping();

builder.Services.AddSingleton<CollectionExecutionTrigger>();
builder.Services.AddHttpClient("ProcessingState", (services, client) =>
    {
        var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    });
builder.Services.AddSingleton<IProcessingStateStore>(services =>
        HttpProcessingStateStoreProxy.Create(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("ProcessingState")));
// JRAサイト再設計（docs/23-jra-scraping-redesign.md）により、旧 JraNavigation/Scrapers.Jra 層に依存する以下の登録は一時的に無効化する。
// builder.Services.AddSingleton<JraResultDateParser>();
// builder.Services.AddSingleton<IJraResultDateDiscoveryService, JraResultMonthDateDiscoveryService>();
builder.Services.AddSingleton<IHistoricalRaceReferenceCollector, NoOpHistoricalRaceReferenceCollector>();
// builder.Services.AddSingleton<IJraRaceResultLookup, JraSiteDataCollectorRaceResultLookup>();
// builder.Services.AddSingleton<IHistoricalRaceResultCollector, JraHistoricalRaceResultCollector>();
// builder.Services.AddSingleton<IJraProfileLookup, JraSiteDataCollectorProfileLookup>();
// builder.Services.AddSingleton<IHistoricalDataRequestHandler, JraHistoricalDataRequestHandler>();
builder.Services.AddTransient<HistoricalDataRequestPlanner>();
builder.Services.AddTransient<HistoricalDataRequestTracker>();

// --once（Lambda）実行時は常駐BackgroundServiceとしてではなく、下記のrunOnce分岐から
// RunOneCycleAsyncを直接1回だけ呼び出す。AddHostedServiceだけではインターフェース越しにしか
// 解決できないため、具象型としても登録しておく。
builder.Services.AddSingleton<ScrapingRegistrationService>();
builder.Services.AddHostedService<ScrapingRegistrationService>(sp => sp.GetRequiredService<ScrapingRegistrationService>());
builder.Services.AddSingleton<CollectionExecutionService>();
builder.Services.AddHostedService<CollectionExecutionService>(sp => sp.GetRequiredService<CollectionExecutionService>());
// builder.Services.AddSingleton<HistoricalDataRequestExecutionService>();
// builder.Services.AddSingleton<CollectionRunCoordinator>();
// builder.Services.AddSingleton<CollectionTaskWorker>();
// if (!runOnce)
// {
//     builder.Services.AddHostedService<LocalCollectionTaskWorkerService>();
// }

var app = builder.Build();

if (runOnce)
{
    // Lambda（SQS event source mapping）から1回呼ばれる経路。常駐BackgroundServiceの
    // ExecuteAsyncループは開始せず、1メッセージ=1ジョブの原則で、このLambda呼び出しを
    // 起こしたSQSメッセージが指すジョブ1件だけを処理して終了する。
    // Lambdaのタイムアウトは15分（infra/collector-lambda/main.tf）。結果報告・
    // ブラウザー終了の猶予として1分だけ残し、14分で打ち切る。
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(14));

    try
    {
        var notification = TryReadTriggeringNotification();
        if (notification is null)
        {
            // トリガーとなったSQSメッセージを読み取れない場合（ローカル実行等）は、
            // 従来通り登録サイクル＋その時点のReadyジョブ全件処理にフォールバックする。
            var registrationService = app.Services.GetRequiredService<ScrapingRegistrationService>();
            await registrationService.RunOneCycleAsync(cts.Token);

            var executionService = app.Services.GetRequiredService<CollectionExecutionService>();
            await executionService.RunOneCycleAsync(cts.Token);
        }
        else if (string.Equals(notification.JobType, AgentJobType.CollectionPlanning, StringComparison.Ordinal))
        {
            // CollectionPlanningジョブは「新規開催日の登録」処理そのものを表すジョブ。
            // 1ジョブ=1Lambda実行の原則に合わせ、このジョブ自体をリースしてから
            // 登録サイクルを1回だけ実行する。
            await RunCollectionPlanningTaskAsync(app.Services, notification, cts.Token);
        }
        else
        {
            var executionService = app.Services.GetRequiredService<CollectionExecutionService>();
            await executionService.RunSingleTaskAsync(notification, cts.Token);
        }
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        // ここに到達するのは、個々のジョブ単位のtry/catch（CollectionExecutionServiceの
        // ジョブループ等）で捕捉されない箇所（例: ScrapingRegistrationServiceのジョブ登録処理
        // 自体や、ジョブ取得のためのHTTP呼び出し）で14分の内部デッドラインに達した場合。
        // ここで捕捉せず素通りさせると、未処理例外としてプロセスがクラッシュ（Aborted (core
        // dumped)）し、bootstrap側は原因不明の固定文言("Collector execution failed")しか
        // Lambdaランタイムへ報告できず、CloudWatch Logs上でタイムアウトか他の異常かの
        // 判別ができなくなる。ここで捕捉し、原因が分かる形でログ出力した上でファイルに書き出し、
        // bootstrapがLambdaランタイムAPIへのエラー応答にその内容を使えるようにする。
        const string reason = "Collector execution timed out (14-minute internal deadline reached).";
        Console.Error.WriteLine(reason);
        try
        {
            await File.WriteAllTextAsync("/tmp/collector-failure-reason.txt", reason);
        }
        catch
        {
            // 失敗理由の書き出し自体の失敗は無視する（bootstrap側は既定の文言にフォールバックする）。
        }

        Environment.Exit(1);
    }
}
else
{
    await app.RunAsync();
}

/// <summary>
/// CollectionPlanningジョブ（新規開催日の登録処理そのもの）をリースし、
/// <see cref="ScrapingRegistrationService.RunOneCycleAsync"/>を1回実行してから、
/// 成功/タイムアウト/失敗に応じてリースの状態を報告する。
/// </summary>
static async Task RunCollectionPlanningTaskAsync(
    IServiceProvider services,
    CollectionTaskNotification notification,
    CancellationToken cancellationToken)
{
    var stateStore = services.GetRequiredService<IProcessingStateStore>();
    var now = DateTimeOffset.UtcNow;
    var task = await stateStore.AcquireCollectionTaskAsync(
        notification.JobType,
        notification.DeduplicationKey,
        notification.DispatchGeneration,
        now,
        TimeSpan.FromMinutes(30),
        cancellationToken).ConfigureAwait(false);

    if (task is null)
    {
        // 既に処理済み/実行中/送出世代が古い。何もせず終了してよい。
        return;
    }

    try
    {
        var registrationService = services.GetRequiredService<ScrapingRegistrationService>();
        await registrationService.RunOneCycleAsync(cancellationToken).ConfigureAwait(false);
        await stateStore.CompleteCollectionTaskAsync(
            notification.JobType, notification.DeduplicationKey, task.LeaseToken, CancellationToken.None).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // 14分の内部デッドラインによる中断。恒久的な失敗ではないため、Readyへ戻し
        // 次回の送出で再試行させる。呼び出し元にキャンセルを伝播し、Lambda実行結果に
        // タイムアウトである旨を明示させる。
        await stateStore.RequeueCollectionTaskAsync(
            notification.JobType,
            notification.DeduplicationKey,
            task.LeaseToken,
            now,
            "Collector execution timed out (14-minute internal deadline reached). Retry scheduled.",
            CancellationToken.None).ConfigureAwait(false);
        throw;
    }
    catch (Exception ex)
    {
        await stateStore.FailCollectionTaskAsync(
            notification.JobType, notification.DeduplicationKey, task.LeaseToken, ex.Message, CancellationToken.None)
            .ConfigureAwait(false);
        throw;
    }
}

/// <summary>
/// bootstrapがLambdaランタイムAPIから受け取り、環境変数 COLLECTOR_EVENT_PATH の指す
/// ファイルへ書き出しておいたSQSイベント（Records[0].body に <see cref="CollectionTaskNotification"/>
/// のJSONが入っている）から、このLambda呼び出しを起こした通知を読み取る。
/// batch_size=1（infra/collector-lambda/main.tf）のため、Recordsは常に0または1件。
/// イベントが存在しない/解析できない場合はnullを返す（呼び出し元は従来の全件処理に
/// フォールバックする）。
/// </summary>
static CollectionTaskNotification? TryReadTriggeringNotification()
{
    var eventPath = Environment.GetEnvironmentVariable("COLLECTOR_EVENT_PATH");
    if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath))
    {
        return null;
    }

    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(eventPath));
        if (!document.RootElement.TryGetProperty("Records", out var records)
            || records.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var firstRecord = records.EnumerateArray().FirstOrDefault();
        if (firstRecord.ValueKind != JsonValueKind.Object
            || !firstRecord.TryGetProperty("body", out var bodyElement)
            || bodyElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<CollectionTaskNotification>(bodyElement.GetString()!, jsonOptions);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to parse the triggering SQS event ({eventPath}): {ex.Message}");
        return null;
    }
}
