namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record JockeyHistoryCollectionRequestPayload(
    string JockeyId,
    string RequestedByRaceId,
    string ProviderType);