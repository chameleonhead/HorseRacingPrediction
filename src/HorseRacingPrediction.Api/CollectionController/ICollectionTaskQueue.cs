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

    /// <summary>
    /// デッドレターキューからメッセージを受信する（削除はしない。処理後に
    /// <see cref="DeleteDeadLetterMessageAsync"/> を呼ぶこと）。
    /// </summary>
    Task<IReadOnlyList<DeadLetterQueueMessage>> ReceiveDeadLetterMessagesAsync(int maxMessages, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DeadLetterQueueMessage>>([]);

    /// <summary>
    /// <see cref="ReceiveDeadLetterMessagesAsync"/> で受信したメッセージを削除し、回収済みとする。
    /// </summary>
    Task DeleteDeadLetterMessageAsync(string receiptHandle, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record DeadLetterQueueMessage(string ReceiptHandle, string Body);
