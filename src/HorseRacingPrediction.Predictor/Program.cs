using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.AgentClient.Scheduling;
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

builder.Services.AddSingleton<ProcessingStateStore>();
builder.Services.AddTransient<HistoricalDataRequestTracker>();
builder.Services.AddTransient<ApiOnlyPredictionWorkflow>();
builder.Services.AddHostedService<PredictionExecutionService>();

var host = builder.Build();
await host.RunAsync();
