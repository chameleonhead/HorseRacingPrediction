namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record HistoricalRequestSummary(
    int PendingHorseRequests,
    int PendingJockeyRequests,
    int PendingRaceResultRequests,
    int PendingTrainerRequests = 0)
{
    public int TotalPendingRequests => PendingHorseRequests + PendingJockeyRequests + PendingRaceResultRequests + PendingTrainerRequests;
}
