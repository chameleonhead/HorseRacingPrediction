// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
// （CollectionExecutionService/ScrapingRegistrationService の無効化に伴うカスケード無効化）
#if false
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class CollectionTaskWorker
{
    private static readonly HashSet<string> HistoricalJobTypes =
    [
        AgentJobType.HistoricalRaceResultCollectionRequest,
        AgentJobType.HorseHistoryCollectionRequest,
        AgentJobType.JockeyHistoryCollectionRequest,
        AgentJobType.TrainerProfileCollectionRequest
    ];

    private readonly IProcessingStateStore _stateStore;
    private readonly ScrapingRegistrationService _planning;
    private readonly CollectionExecutionService _collection;
    private readonly HistoricalDataRequestExecutionService _historical;

    public CollectionTaskWorker(
        IProcessingStateStore stateStore,
        ScrapingRegistrationService planning,
        CollectionExecutionService collection,
        HistoricalDataRequestExecutionService historical)
    {
        _stateStore = stateStore;
        _planning = planning;
        _collection = collection;
        _historical = historical;
    }

    public async Task<bool> RunAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
    {
        var task = await _stateStore.AcquireCollectionTaskAsync(
            notification.JobType,
            notification.DeduplicationKey,
            notification.DispatchGeneration,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10),
            cancellationToken).ConfigureAwait(false);
        if (task is null) return false;

        using var scope = HttpProcessingStateStoreProxy.BeginTaskScope(task);
        try
        {
            if (task.JobType == AgentJobType.CollectionPlanning)
            {
                await _planning.RunOneCycleAsync(cancellationToken).ConfigureAwait(false);
                await _stateStore.CompleteJobAsync(task.JobType, task.DeduplicationKey, cancellationToken).ConfigureAwait(false);
            }
            else if (HistoricalJobTypes.Contains(task.JobType))
            {
                await _historical.RunTaskAsync(task.JobType, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _collection.RunTaskAsync(task.JobType, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }
        catch (Exception ex)
        {
            var failed = await _stateStore.FailCollectionTaskAsync(
                task.JobType,
                task.DeduplicationKey,
                task.LeaseToken,
                ex.Message,
                CancellationToken.None).ConfigureAwait(false);
            if (!failed)
                throw new InvalidOperationException("The collection task failed, but its failed state could not be persisted.", ex);
            return false;
        }
    }

    public static CollectionTaskNotification? ReadLambdaNotification(string? eventPath)
    {
        if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(eventPath));
        if (!document.RootElement.TryGetProperty("Records", out var records) || records.GetArrayLength() == 0) return null;
        var body = records[0].GetProperty("body").GetString();
        return string.IsNullOrWhiteSpace(body)
            ? null
            : JsonSerializer.Deserialize<CollectionTaskNotification>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}

public sealed class LocalCollectionTaskWorkerService : BackgroundService
{
    private readonly IProcessingStateStore _stateStore;
    private readonly CollectionTaskWorker _worker;

    public LocalCollectionTaskWorkerService(IProcessingStateStore stateStore, CollectionTaskWorker worker)
    {
        _stateStore = stateStore;
        _worker = worker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatches = await _stateStore.GetPendingCollectionTaskDispatchesAsync(DateTimeOffset.UtcNow, 1, stoppingToken)
                    .ConfigureAwait(false);
                if (dispatches.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var dispatch = dispatches[0];
                await _stateStore.MarkCollectionTaskDispatchedAsync(dispatch.OutboxId, DateTimeOffset.UtcNow, stoppingToken)
                    .ConfigureAwait(false);
                try
                {
                    await _worker.RunAsync(dispatch.Notification, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await _stateStore.MarkCollectionTaskDispatchFailedAsync(dispatch.OutboxId, DateTimeOffset.Now, ex.Message, stoppingToken);
                }
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
#endif
