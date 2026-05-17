namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record ResultDayDiscoveryRequestPayload(
    DateOnly RaceDate,
    string ProviderType);