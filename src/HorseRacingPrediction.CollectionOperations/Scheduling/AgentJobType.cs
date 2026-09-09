namespace HorseRacingPrediction.Collector.Scheduling;

public static class AgentJobType
{
    public const string SubjectProfileRefresh = "SubjectProfileRefresh";
    public const string HorseHistoryDiscovery = "HorseHistoryDiscovery";
    public const string HorseHistoryRace = "HorseHistoryRace";
    public const string HorseHistoryExcluded = "HorseHistoryExcluded";
    public const string RaceReacquisition = "RaceReacquisition";
    public const string RaceDayReacquisition = "RaceDayReacquisition";
    public const string CollectionPlanning = "CollectionPlanning";
    public const string AcquisitionPlanReview = "AcquisitionPlanReview";
    public const string ResultBackfillPlanningRequest = "ResultBackfillPlanningRequest";
    public const string ResultMonthDiscoveryRequest = "ResultMonthDiscoveryRequest";
    public const string ResultDayDiscoveryRequest = "ResultDayDiscoveryRequest";
    public const string ResultDayCollectionRequest = "ResultDayCollectionRequest";
    public const string HistoricalRaceResultCollectionRequest = "HistoricalRaceResultCollectionRequest";
    public const string HorseHistoryCollectionRequest = "HorseHistoryCollectionRequest";
    public const string JockeyHistoryCollectionRequest = "JockeyHistoryCollectionRequest";
    public const string TrainerProfileCollectionRequest = "TrainerProfileCollectionRequest";
    public const string RaceCardCollection = "RaceCardCollection";
    public const string RaceResultCollection = "RaceResultCollection";
    public const string PredictionExecution = "PredictionExecution";
}
