namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionJobWatchdogOptions
{
    public const string SectionName = "CollectionJobWatchdog";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Ready ジョブへの再ディスパッチ（SQS再送出）を許容する最大回数。
    /// これを超えたジョブは DeadLetter として打ち切り、再送を止める（Fail Fast）。
    /// </summary>
    public int MaxJobDispatchAttempts { get; set; } = 5;

    /// <summary>
    /// このメッセージ数以上が収集ジョブ用DLQに滞留している場合、実行基盤（Lambda/コンテナ等）側で
    /// 恒常的な失敗が起きているとみなし、新規ジョブも含めたSQSへのディスパッチを一時停止する
    /// （サーキットブレーカー）。個別ジョブの再送止め（<see cref="MaxJobDispatchAttempts"/>）と異なり、
    /// システム全体の異常時に無駄なSQS送出・Lambda起動コストが際限なく積み上がるのを防ぐ。
    /// </summary>
    public int DeadLetterQueueDepthCircuitBreakerThreshold { get; set; } = 20;
}
