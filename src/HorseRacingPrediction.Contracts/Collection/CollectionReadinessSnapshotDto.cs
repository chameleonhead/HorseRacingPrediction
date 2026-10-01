namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionReadinessSnapshotDto(int PendingHorseRequests, int PendingJockeyRequests,
    int PendingRaceResultRequests, int PendingTrainerRequests)
{
    public int TotalPendingRequests => PendingHorseRequests + PendingJockeyRequests
        + PendingRaceResultRequests + PendingTrainerRequests;
}
