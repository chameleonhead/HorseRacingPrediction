using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Predictor.Scheduling;
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
builder.Services.AddSingleton<IProcessingStateStore>(services => services.GetRequiredService<ProcessingStateStore>());
builder.Services.AddTransient<HistoricalDataRequestTracker>();
builder.Services.AddTransient<ApiOnlyPredictionWorkflow>();

// フェーズ2: ストーリー仕立て SNS 投稿文生成（LLM 使用、予想票確定ごとに低頻度実行）
builder.Services.Configure<PostGenerationOptions>(
    builder.Configuration.GetSection(PostGenerationOptions.SectionName));
builder.Services.AddLMStudioChatClient();
builder.Services.AddPostGenerationWorkflow();
builder.Services.AddTransient<PostGenerationExecutionStep>();

builder.Services.AddHostedService<HorseRacingPrediction.Predictor.Scheduling.PredictionExecutionService>();

var host = builder.Build();
await host.RunAsync();
