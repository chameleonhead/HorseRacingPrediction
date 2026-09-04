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
// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層に依存する以下の登録は一時的に無効化する。
// builder.Services.AddSingleton<JraResultDateParser>();
// builder.Services.AddSingleton<IJraResultDateDiscoveryService, JraResultMonthDateDiscoveryService>();
builder.Services.AddSingleton<IHistoricalRaceReferenceCollector, NoOpHistoricalRaceReferenceCollector>();
// builder.Services.AddSingleton<IJraRaceResultLookup, JraSiteDataCollectorRaceResultLookup>();
// builder.Services.AddSingleton<IHistoricalRaceResultCollector, JraHistoricalRaceResultCollector>();
// builder.Services.AddSingleton<IJraProfileLookup, JraSiteDataCollectorProfileLookup>();
// builder.Services.AddSingleton<IHistoricalDataRequestHandler, JraHistoricalDataRequestHandler>();
builder.Services.AddTransient<HistoricalDataRequestPlanner>();
builder.Services.AddTransient<HistoricalDataRequestTracker>();

builder.Services.AddHostedService<ScrapingRegistrationService>();
builder.Services.AddHostedService<CollectionExecutionService>();
// builder.Services.AddSingleton<HistoricalDataRequestExecutionService>();
// builder.Services.AddSingleton<CollectionRunCoordinator>();
// builder.Services.AddSingleton<CollectionTaskWorker>();
// if (!runOnce)
// {
//     builder.Services.AddHostedService<LocalCollectionTaskWorkerService>();
// }

var app = builder.Build();

// JRAサイト再設計により CollectionTaskWorker 一式が一時的に無効化されているため、
// --once/常駐いずれの実行経路も一時的に無効化する。
if (runOnce)
{
    throw new InvalidOperationException("Collector の --once 実行は Jra 再設計に伴い一時的に無効化されています（docs/jra-scraping.md 参照）。");
}
else
{
    await app.RunAsync();
}
