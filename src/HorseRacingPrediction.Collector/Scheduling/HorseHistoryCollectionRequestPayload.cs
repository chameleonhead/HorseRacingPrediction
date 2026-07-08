namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record HorseHistoryCollectionRequestPayload(
    string HorseId,
    string RequestedByRaceId,
    string ProviderType);