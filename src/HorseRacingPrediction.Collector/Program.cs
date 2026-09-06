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
    // ExecuteAsyncループは開始せず、ジョブ登録（新規開催日の発見）とジョブ実行
    // （Ready状態のRaceCard/RaceResult収集ジョブの処理）を1サイクルずつ直接呼び出して終了する。
    // Lambdaのタイムアウトは15分（infra/collector-lambda/main.tf）。結果報告・
    // ブラウザー終了の猶予として1分だけ残し、14分で打ち切る。
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(14));

    try
    {
        var registrationService = app.Services.GetRequiredService<ScrapingRegistrationService>();
        await registrationService.RunOneCycleAsync(cts.Token);

        var executionService = app.Services.GetRequiredService<CollectionExecutionService>();
        await executionService.RunOneCycleAsync(cts.Token);
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
