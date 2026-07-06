using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Collector.JraTesting;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Collector.Web.Components;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

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

await app.RunAsync();
