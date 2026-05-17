namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record RaceCardCollectionJobPayload(
    DateOnly RaceDate,
    string ProviderType);