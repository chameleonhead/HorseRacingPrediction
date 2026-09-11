using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class NullCollectionTaskQueue : ICollectionTaskQueue, ICollectionPlatformTaskQueue
{
    public Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("CollectionQueue is not configured.");

    Task ICollectionPlatformTaskQueue.SendAsync(
        HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionTaskNotification notification,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("CollectionQueue is not configured.");

    public Task PurgeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
