namespace HorseRacingPrediction.Collector.Scheduling;

public static class AgentJobType
{
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
