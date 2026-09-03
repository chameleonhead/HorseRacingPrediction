namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record ResultDayDiscoveryRequestPayload(
    DateOnly RaceDate,
    string ProviderType);