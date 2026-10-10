namespace HorseRacingPrediction.Api.CollectionController;

internal sealed class CollectionBackgroundRuntimeOptions
{
    internal static readonly TimeSpan ProducerCadence = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan ProducerSliceBudget = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ClockJumpTolerance = TimeSpan.FromMinutes(1);
}
