namespace HorseRacingPrediction.Api.CollectionController;

public sealed class NullCollectionTaskQueue : ICollectionTaskQueue, ICollectionPlatformTaskQueue
{
    Task<CollectionQueueSendReceipt> ICollectionPlatformTaskQueue.SendAsync(
        HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionDispatchEnvelope envelope,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("CollectionQueue is not configured.");

    public Task PurgeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
