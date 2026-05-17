namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record HistoricalDataRequestPlan(
    int RequestedHorseHistoryCount,
    int RequestedJockeyHistoryCount,
    int RequestedRaceResultCount);