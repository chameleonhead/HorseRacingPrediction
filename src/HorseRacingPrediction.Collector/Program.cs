using System.Linq;
using System.Text.Json;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using HorseRacingPrediction.PredictionScheduling;

var builder = Host.CreateApplicationBuilder(args);
var runOnce = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
var runLocalQueue = args.Contains("--local-queue", StringComparer.OrdinalIgnoreCase);

builder.Services.Configure<ApiClientOptions>(
    builder.Configuration.GetSection(ApiClientOptions.SectionName));
builder.Services.AddSingleton<IValidateOptions<ApiClientOptions>, ApiClientOptionsValidator>();
builder.Services.AddOptions<ApiClientOptions>()
    .Bind(builder.Configuration.GetSection(ApiClientOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddHttpAgentServices();

builder.Services.AddJraScraping();
builder.Services.AddSingleton<ICollectionDefinitionHandler, JraRaceCardCollectionHandler>();
builder.Services.AddSingleton<ICollectionDefinitionHandler, JraRaceResultCollectionHandler>();
builder.Services.Configure<RaceDiscoveryCollectionOptions>(builder.Configuration.GetSection("RaceDiscoveryCollection"));
builder.Services.AddSingleton<ICollectionDefinitionHandler, JraRaceDiscoveryCollectionHandler>();
builder.Services.Configure<RaceOddsCollectionOptions>(builder.Configuration.GetSection("RaceOddsCollection"));
builder.Services.AddSingleton<ICollectionDefinitionHandler, JraRaceOddsCollectionHandler>();
foreach (var descriptor in JraSubjectCollectionDefinitions.All)
    builder.Services.AddSingleton<ICollectionDefinitionHandler>(services =>
        new JraSubjectProfileCollectionHandler(descriptor,
            services.GetRequiredService<IJraSessionFactory>(),
            services.GetRequiredService<IJraSubjectProfileSink>(),
            services.GetRequiredService<ICollectionRequestSink>()));
builder.Services.AddSingleton<CollectionDefinitionHandlerRegistry>();

builder.Services.AddHttpClient<CollectionPlatformWorkerClient>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    })
    .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
builder.Services.AddHttpClient<CollectionRequestApiClient>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    })
    .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
builder.Services.AddSingleton<ICollectionRequestSink>(services =>
    services.GetRequiredService<CollectionRequestApiClient>());
builder.Services.AddHttpClient<JraSubjectProfileApiClient>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    })
    .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
builder.Services.AddSingleton<IJraSubjectProfileSink>(services =>
    services.GetRequiredService<JraSubjectProfileApiClient>());
builder.Services.AddHttpClient<RaceOddsSnapshotApiClient>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    }).AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
builder.Services.AddSingleton<IRaceOddsSnapshotSink>(services => services.GetRequiredService<RaceOddsSnapshotApiClient>());
builder.Services.AddHttpClient<IPredictionSchedule, HttpPredictionSchedule>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<ApiClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    })
    .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
var app = builder.Build();

if (runLocalQueue)
{
    var queuePath = builder.Configuration["LocalQueue:DatabasePath"]
        ?? "collection-platform-state/local-collection-queue.db";
    var queue = new LocalCollectionQueue(queuePath);
    while (true)
    {
        var message = await queue.ReceiveAsync(TimeSpan.FromMinutes(15), CancellationToken.None);
        if (message is null) { await Task.Delay(TimeSpan.FromSeconds(1)); continue; }
        try
        {
            var worker = app.Services.GetRequiredService<CollectionPlatformWorkerClient>();
            var sessionFactory = app.Services.GetRequiredService<IJraSessionFactory>();
            await JraSessionExecutionScope.ExecuteAsync(sessionFactory, async cancellationToken =>
            {
                var executionBatchId = Guid.NewGuid();
                for (var index = 0; index < message.Envelope.Tasks.Count; index++)
                {
                    var task = message.Envelope.Tasks[index];
                    using var correlation = CollectionAttemptCorrelationScope.Push(new(executionBatchId,
                        message.Envelope.EnvelopeId, $"local-{message.MessageId}", null,
                        index + 1, message.Envelope.Tasks.Count));
                    await worker.ExecuteAsync(new(task.TaskId, task.DispatchGeneration), cancellationToken)
                        .ConfigureAwait(false);
                }
            }, CancellationToken.None, message.Envelope.Compatibility).ConfigureAwait(false);
            await queue.AcknowledgeAsync(message.ReceiptHandle).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Local queue delivery failed: {ex.Message}");
            await queue.ReleaseAsync(message.ReceiptHandle).ConfigureAwait(false);
        }
    }
}
else if (runOnce)
{
    // Lambda（SQS event source mapping）から1回呼ばれる経路。1 SQS message内の
    // 互換Task envelopeを逐次処理し、未解決messageだけを部分失敗応答へ含める。
    // Lambdaのタイムアウトは15分（infra/collector-lambda/main.tf）。結果報告・
    // ブラウザー終了の猶予として1分だけ残し、14分で打ち切る。
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(14));

    // CloudWatch Logsで該当する呼び出しをすぐに検索できるよう、失敗メッセージに
    // Lambda RequestIdを含める。bootstrapがLambdaランタイムAPIから取得したRequestIdを
    // 環境変数で渡す。ローカル実行等でこの環境変数が無い場合は、混同を避けつつも
    // 目視で「ローカル実行由来」と分かる仮のIDを生成する。
    var requestId = Environment.GetEnvironmentVariable("AWS_LAMBDA_REQUEST_ID");
    if (string.IsNullOrWhiteSpace(requestId))
    {
        requestId = $"local-{Guid.NewGuid():N}";
    }

    try
    {
        var eventPath = Environment.GetEnvironmentVariable("COLLECTOR_EVENT_PATH");
        if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath))
            throw new InvalidOperationException("A resource collection SQS event is required.");
        var worker = app.Services.GetRequiredService<CollectionPlatformWorkerClient>();
        var sessionFactory = app.Services.GetRequiredService<IJraSessionFactory>();
        var response = await CollectionLambdaInvocation.ExecuteAsync(await File.ReadAllTextAsync(eventPath, cts.Token),
            worker.ExecuteAsync, HasLambdaTimeRemaining, cts.Token,
             (envelope, operation, cancellationToken) => JraSessionExecutionScope.ExecuteAsync(
                  sessionFactory, operation, cancellationToken, envelope.Compatibility),
             requestId).ConfigureAwait(false);
        var responsePath = Environment.GetEnvironmentVariable("COLLECTOR_RESPONSE_PATH")
            ?? "/tmp/collector-response.json";
        await File.WriteAllTextAsync(responsePath, CollectionLambdaInvocation.SerializeResponse(response),
            CancellationToken.None).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && cts.IsCancellationRequested))
    {
        // CollectionPlatformWorkerClient reports a retryable attempt with an independent reporting deadline.
        var reason = ex is TimeoutException ? ex.Message : $"Collector execution timed out (14-minute internal deadline reached). RequestId={requestId}";
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
    throw new InvalidOperationException("Collector requires --once and a resource collection notification.");
}

static bool HasLambdaTimeRemaining()
{
    var value = Environment.GetEnvironmentVariable("AWS_LAMBDA_DEADLINE_MS");
    return !long.TryParse(value, out var deadlineMilliseconds)
           || DateTimeOffset.UtcNow < DateTimeOffset.FromUnixTimeMilliseconds(deadlineMilliseconds).AddMinutes(-1);
}
