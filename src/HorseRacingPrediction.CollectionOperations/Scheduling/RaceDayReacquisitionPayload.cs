namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record RaceDayReacquisitionPayload(
    DateOnly RaceDate,
    string ProviderType = "JRA",
    string? Reason = null);
