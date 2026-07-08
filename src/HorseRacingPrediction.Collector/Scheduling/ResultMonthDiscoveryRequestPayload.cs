namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record ResultMonthDiscoveryRequestPayload(
    string ProviderType,
    int Year,
    int Month,
    bool RevisitIncompleteDays);