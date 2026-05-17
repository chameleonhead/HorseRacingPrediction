namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record HistoricalRequestSummary(
    int PendingHorseRequests,
    int PendingJockeyRequests,
    int PendingRaceResultRequests)
{
    public int TotalPendingRequests => PendingHorseRequests + PendingJockeyRequests + PendingRaceResultRequests;
}