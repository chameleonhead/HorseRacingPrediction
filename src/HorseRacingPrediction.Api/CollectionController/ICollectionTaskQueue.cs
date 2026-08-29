using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.CollectionController;

public interface ICollectionTaskQueue
{
    Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken);
    Task PurgeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
