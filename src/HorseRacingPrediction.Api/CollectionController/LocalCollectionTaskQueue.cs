using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class LocalCollectionTaskQueue(LocalCollectionQueue queue) : ICollectionTaskQueue, ICollectionPlatformTaskQueue
{
    Task ICollectionPlatformTaskQueue.SendAsync(CollectionTaskNotification notification, CancellationToken token)
        => queue.SendAsync(notification, token);

    public async Task<CollectionQueueDepth> GetQueueDepthAsync(CancellationToken token)
    {
        var depth = await queue.GetDepthAsync(token);
        return new(depth.Visible, depth.NotVisible);
    }

    public async Task<long> GetDeadLetterQueueDepthAsync(CancellationToken token)
        => (await queue.GetDepthAsync(token)).DeadLetter;
}
