namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionMaintenanceState
{
    private int _active;
    private int _dlqFailureCount;
    public bool IsActive => Volatile.Read(ref _active) == 1;
    public int DlqFailureCount => Volatile.Read(ref _dlqFailureCount);
    public bool TryBegin() => Interlocked.CompareExchange(ref _active, 1, 0) == 0;

    public void End()
    {
        Volatile.Write(ref _active, 0);
        Volatile.Write(ref _dlqFailureCount, 0);
    }

    /// <summary>
    /// DLQから回収し失敗として確定させたジョブの累計件数を1件加算する。
    /// /resume（<see cref="End"/>）が呼ばれるまでリセットされない。
    /// </summary>
    public int RecordDlqFailure() => Interlocked.Increment(ref _dlqFailureCount);
}
