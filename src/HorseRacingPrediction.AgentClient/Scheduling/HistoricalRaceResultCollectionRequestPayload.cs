namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record HistoricalRaceResultCollectionRequestPayload(
    DateOnly RaceDate,
    string Racecourse,
    int RaceNumber,
    string RequestedByRaceId,
    string ProviderType);