namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionMaintenanceState
{
    private int _active;
    public bool IsActive => Volatile.Read(ref _active) == 1;
    public bool TryBegin() => Interlocked.CompareExchange(ref _active, 1, 0) == 0;
    public void End() => Volatile.Write(ref _active, 0);
}
