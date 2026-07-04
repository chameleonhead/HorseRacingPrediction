using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddSingleton<ProcessingStateStore>();
builder.Services.AddSingleton<JraResultDateParser>();
builder.Services.AddSingleton<IJraResultDateDiscoveryService, JraResultMonthDateDiscoveryService>();
builder.Services.AddSingleton<IHistoricalRaceReferenceCollector, JraHistoricalRaceReferenceCollector>();
builder.Services.AddSingleton<IJraRaceResultLookup, JraSiteDataCollectorRaceResultLookup>();
builder.Services.AddSingleton<IHistoricalRaceResultCollector, JraHistoricalRaceResultCollector>();
builder.Services.AddSingleton<IJraProfileLookup, JraSiteDataCollectorProfileLookup>();
builder.Services.AddSingleton<IHistoricalDataRequestHandler, JraHistoricalDataRequestHandler>();
builder.Services.AddTransient<HistoricalDataRequestPlanner>();

builder.Services.AddHostedService<ScrapingRegistrationService>();
builder.Services.AddHostedService<CollectionExecutionService>();
builder.Services.AddHostedService<HistoricalDataRequestExecutionService>();

var host = builder.Build();
await host.RunAsync();
