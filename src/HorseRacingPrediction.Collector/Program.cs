using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Collector.JraTesting;
using HorseRacingPrediction.Collector.Web.Components;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Workflow;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
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

builder.Services.AddSingleton<IWebBrowserSessionFactory, PlaywrightWebBrowserSessionFactory>();
builder.Services.AddJraRaceScheduleCollectionWorkflow();

builder.Services.AddSingleton<CollectionExecutionTrigger>();
if (builder.Configuration.GetValue<bool>($"{AgentProcessingOptions.SectionName}:UseApiStateStore", true))
{
    builder.Services.AddHttpClient("ProcessingState", (services, client) =>
    {
        var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    });
    builder.Services.AddSingleton<IProcessingStateStore>(services =>
        HttpProcessingStateStoreProxy.Create(
            services.GetRequiredService<IHttpClientFactory>().CreateClient("ProcessingState")));
}
else
{
    builder.Services.AddSingleton<ProcessingStateStore>();
    builder.Services.AddSingleton<IProcessingStateStore>(services => services.GetRequiredService<ProcessingStateStore>());
}
builder.Services.AddSingleton<JraResultDateParser>();
builder.Services.AddSingleton<IJraResultDateDiscoveryService, JraResultMonthDateDiscoveryService>();
builder.Services.AddSingleton<IHistoricalRaceReferenceCollector, JraHistoricalRaceReferenceCollector>();
builder.Services.AddSingleton<IJraRaceResultLookup, JraSiteDataCollectorRaceResultLookup>();
builder.Services.AddSingleton<IHistoricalRaceResultCollector, JraHistoricalRaceResultCollector>();
builder.Services.AddSingleton<IJraProfileLookup, JraSiteDataCollectorProfileLookup>();
builder.Services.AddSingleton<IHistoricalDataRequestHandler, JraHistoricalDataRequestHandler>();
builder.Services.AddTransient<HistoricalDataRequestPlanner>();
builder.Services.AddTransient<HistoricalDataRequestTracker>();

builder.Services.AddSingleton<ScrapingRegistrationService>();
builder.Services.AddSingleton<CollectionExecutionService>();
builder.Services.AddSingleton<HistoricalDataRequestExecutionService>();
builder.Services.AddSingleton<CollectionRunCoordinator>();
builder.Services.AddSingleton<CollectionTaskWorker>();
if (!runOnce)
{
    builder.Services.AddHostedService<LocalCollectionTaskWorkerService>();
}

// -------------------------------------------------------------------
// JRA 抽出デバッグツール
// -------------------------------------------------------------------
builder.Services.AddSingleton<JraJsonExtractionService>();

// -------------------------------------------------------------------
// 収集状況ダッシュボード（Blazor Server）
// -------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapAgentDashboardEndpoints();
app.MapAgentCollectionStatusEndpoints();
app.MapAgentAcquisitionStatusEndpoints();
app.MapJraJsonTesterEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (runOnce)
{
    var notification = CollectionTaskWorker.ReadLambdaNotification(
        Environment.GetEnvironmentVariable("COLLECTOR_EVENT_PATH"));
    if (notification is null)
        throw new InvalidOperationException("A collection task notification is required for --once execution.");

    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(9));
    await app.Services.GetRequiredService<CollectionTaskWorker>().RunAsync(notification, deadline.Token);
}
else
{
    await app.RunAsync();
}
