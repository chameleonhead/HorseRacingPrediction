using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.CollectionController;

public interface ICollectionTaskQueue
{
    Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken);
    Task PurgeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// デッドレターキューに滞留しているメッセージ数（概算）を取得する。
    /// 実装がない/取得できない場合は0を返し、呼び出し側のサーキットブレーカーは働かない。
    /// </summary>
    Task<long> GetDeadLetterQueueDepthAsync(CancellationToken cancellationToken) => Task.FromResult(0L);
}
