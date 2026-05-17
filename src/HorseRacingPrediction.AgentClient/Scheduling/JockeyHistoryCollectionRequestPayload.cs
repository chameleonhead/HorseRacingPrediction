namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record JockeyHistoryCollectionRequestPayload(
    string JockeyId,
    string RequestedByRaceId,
    string ProviderType);