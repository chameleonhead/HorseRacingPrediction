namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record HorseHistoryCollectionRequestPayload(
    string HorseId,
    string RequestedByRaceId,
    string ProviderType);