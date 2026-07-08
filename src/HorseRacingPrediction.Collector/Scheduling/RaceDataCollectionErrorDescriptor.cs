namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record RaceDataCollectionErrorDescriptor(
    RaceDataCollectionErrorCode Code,
    string Reason);