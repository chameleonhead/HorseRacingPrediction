namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record HistoricalRaceReference(
    DateOnly RaceDate,
    string Racecourse,
    int RaceNumber);