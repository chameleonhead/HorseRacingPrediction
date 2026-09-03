using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Workflow;
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

builder.Services.AddSingleton<IWebBrowserSessionFactory, PlaywrightWebBrowserSessionFactory>();
builder.Services.AddJraRaceScheduleCollectionWorkflow();

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

var app = builder.Build();

if (runOnce)
{
    var notification = CollectionTaskWorker.ReadLambdaNotification(
        Environment.GetEnvironmentVariable("COLLECTOR_EVENT_PATH"));
    if (notification is null)
        throw new InvalidOperationException("A collection task notification is required for --once execution.");

    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(9));
    var succeeded = await app.Services.GetRequiredService<CollectionTaskWorker>().RunAsync(notification, deadline.Token);
    if (!succeeded)
        throw new InvalidOperationException($"Collection task failed: {notification.JobType}:{notification.DeduplicationKey}");
}
else
{
    await app.RunAsync();
}
