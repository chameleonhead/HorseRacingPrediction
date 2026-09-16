using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class LocalCollectionTaskQueue(LocalCollectionQueue queue) : ICollectionTaskQueue, ICollectionPlatformTaskQueue
{
    async Task<CollectionQueueSendReceipt> ICollectionPlatformTaskQueue.SendAsync(
        CollectionDispatchEnvelope envelope, CancellationToken token)
        => new((await queue.SendAsync(envelope, token).ConfigureAwait(false)).ToString());

    async Task<CollectionQueueSendReceipt> ICollectionPlatformTaskQueue.SendWakeAsync(
        CollectionWakeSignal wake, CancellationToken token)
        => new((await queue.SendWakeAsync(wake, token).ConfigureAwait(false)).ToString());

    public async Task<CollectionQueueDepth> GetQueueDepthAsync(CancellationToken token)
    {
        var depth = await queue.GetDepthAsync(token);
        return new(depth.Visible, depth.NotVisible);
    }

    public async Task<long> GetDeadLetterQueueDepthAsync(CancellationToken token)
        => (await queue.GetDepthAsync(token)).DeadLetter;
}
