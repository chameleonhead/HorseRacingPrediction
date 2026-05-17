namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record HistoricalRaceReference(
    DateOnly RaceDate,
    string Racecourse,
    int RaceNumber);