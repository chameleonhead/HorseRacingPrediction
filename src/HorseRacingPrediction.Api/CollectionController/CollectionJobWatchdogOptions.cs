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
    /// ジョブが直近にSQSへディスパッチされてから、まだWorker（Lambda）がリースを
    /// 取得しておらず Ready のまま残っている場合でも、この分数が経過するまでは
    /// 再ディスパッチしない猶予期間。Workerが未着手なだけの正常なジョブを二重に
    /// SQSへ送出してしまい、実際には1回しか失敗していないのに再送出回数
    /// （<see cref="MaxJobDispatchAttempts"/>）だけが余分に積み上がるのを防ぐ。
    /// Lambdaの実行時間（最大15分）を考慮した値にすること。
    /// </summary>
    public int DispatchGraceMinutes { get; set; } = 20;

    /// <summary>
    /// このメッセージ数以上が収集ジョブ用DLQに滞留している場合、実行基盤（Lambda/コンテナ等）側で
    /// 恒常的な失敗が起きているとみなし、新規ジョブも含めたSQSへのディスパッチを一時停止する
    /// （サーキットブレーカー）。個別ジョブの再送止め（<see cref="MaxJobDispatchAttempts"/>）と異なり、
    /// システム全体の異常時に無駄なSQS送出・Lambda起動コストが際限なく積み上がるのを防ぐ。
    /// </summary>
    public int DeadLetterQueueDepthCircuitBreakerThreshold { get; set; } = 20;
}
