namespace HorseRacingPrediction.Api.CollectionController;

public sealed class NullCollectionTaskQueue : ICollectionTaskQueue, ICollectionPlatformTaskQueue
{
    Task ICollectionPlatformTaskQueue.SendAsync(
        HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionTaskNotification notification,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("CollectionQueue is not configured.");

    public Task PurgeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
