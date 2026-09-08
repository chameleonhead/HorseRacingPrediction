namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionMaintenanceState
{
    private int _active;
    private int _collectorOnly;
    private int _dlqFailureCount;
    public bool IsActive => Volatile.Read(ref _active) == 1;

    /// <summary>
    /// true: collection dispatch/acquisition is stopped by the persisted store state;
    /// active workers can still report results. false: full database maintenance
    /// rejects mutations, including collection result reporting.
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
