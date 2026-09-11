using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Predictor.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.PredictionScheduling;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ApiClientOptions>(
    builder.Configuration.GetSection(ApiClientOptions.SectionName));
builder.Services.AddSingleton<IValidateOptions<ApiClientOptions>, ApiClientOptionsValidator>();
builder.Services.AddOptions<ApiClientOptions>()
    .Bind(builder.Configuration.GetSection(ApiClientOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddHttpAgentServices();

builder.Services.Configure<PredictionExecutionOptions>(
    builder.Configuration.GetSection(PredictionExecutionOptions.SectionName));

builder.Services.AddHttpClient<IPredictionSchedule, HttpPredictionSchedule>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
}).AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
builder.Services.AddHttpClient<CollectionReadinessClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
}).AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
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
