namespace HorseRacingPrediction.Api.CollectionController;

/// <summary>
/// DLQ（デッドレターキュー）の滞留状況に基づくサーキットブレーカーの状態を、
/// <see cref="CollectionJobWatchdogService"/>（判定・トリップ）と
/// <see cref="CollectionTaskOutboxDispatcher"/>（トリップ中は新規ディスパッチを止める）の
/// 間で共有するためのシングルトン。
/// </summary>
public sealed class CollectionQueueCircuitBreakerState
{
    private volatile bool _isTripped;

    public bool IsTripped => _isTripped;

    public long LastKnownDeadLetterQueueDepth { get; private set; }

    public void Trip(long deadLetterQueueDepth)
    {
        _isTripped = true;
        LastKnownDeadLetterQueueDepth = deadLetterQueueDepth;
    }

    public void Reset(long deadLetterQueueDepth)
    {
        _isTripped = false;
        LastKnownDeadLetterQueueDepth = deadLetterQueueDepth;
    }
}
