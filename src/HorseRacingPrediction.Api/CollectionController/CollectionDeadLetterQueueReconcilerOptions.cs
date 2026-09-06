namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionDeadLetterQueueReconcilerOptions
{
    public const string SectionName = "CollectionDeadLetterQueueReconciler";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 30;

    public int MaxMessagesPerCycle { get; set; } = 10;

    /// <summary>
    /// DLQから回収し Failed とマークした累計件数がこれに達したら、収集ジョブ全体を停止する
    /// （メンテナンスモードに入りSQSキューをパージ、収集停止アラートをSNSへ送信）。
    /// /resume が呼ばれるまでカウントはリセットされない。
    /// </summary>
    public int ConsecutiveFailureThreshold { get; set; } = 3;
}
