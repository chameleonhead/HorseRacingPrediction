namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record RaceCardCollectionJobPayload(
    DateOnly RaceDate,
    string ProviderType);