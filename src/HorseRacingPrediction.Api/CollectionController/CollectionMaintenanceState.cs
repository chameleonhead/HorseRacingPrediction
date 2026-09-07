namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionMaintenanceState
{
    private int _active;
    private int _collectorOnly;
    private int _dlqFailureCount;
    public bool IsActive => Volatile.Read(ref _active) == 1;

    /// <summary>
    /// true の場合、一時停止の影響範囲はCollector（データ収集エージェント）からの
    /// リクエストのみに限定される（Collectorが最初に呼ぶ内部RPCエンドポイント
    /// <c>/api/internal/collection/state/*</c> のみを503で拒否し、それ以外の
    /// 管理画面等からの書き込みは通常通り許可する）。false の場合は従来通り、
    /// 全ての書き込み系エンドポイントを拒否する（データベース完全初期化中など、
    /// システム全体を止める必要がある操作向け）。
    /// </summary>
    public bool IsCollectorOnly => Volatile.Read(ref _collectorOnly) == 1;
    public int DlqFailureCount => Volatile.Read(ref _dlqFailureCount);

    public bool TryBegin(bool collectorOnly = false)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            return false;
        }

        Volatile.Write(ref _collectorOnly, collectorOnly ? 1 : 0);
        return true;
    }

    public void End()
    {
        Volatile.Write(ref _active, 0);
        Volatile.Write(ref _collectorOnly, 0);
        Volatile.Write(ref _dlqFailureCount, 0);
    }

    /// <summary>
    /// DLQから回収し失敗として確定させたジョブの累計件数を1件加算する。
    /// /resume（<see cref="End"/>）が呼ばれるまでリセットされない。
    /// </summary>
    public int RecordDlqFailure() => Interlocked.Increment(ref _dlqFailureCount);
}
