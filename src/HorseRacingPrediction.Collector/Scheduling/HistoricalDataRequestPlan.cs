namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record HistoricalDataRequestPlan(
    int RequestedHorseHistoryCount,
    int RequestedJockeyHistoryCount,
    int RequestedRaceResultCount);