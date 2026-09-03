using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Collector.Scheduling;

public static class AgentJobKeyFactory
{
    public static string BuildResultBackfillPlanningRequestKey(string providerType)
        => $"{providerType}:result-backfill-plan";

    public static string BuildResultMonthDiscoveryRequestKey(string providerType, int year, int month)
        => $"{providerType}:result-month:{year:D4}-{month:D2}";

    public static string BuildResultDayDiscoveryRequestKey(string providerType, DateOnly raceDate)
        => $"{providerType}:result-day-discovery:{raceDate:yyyy-MM-dd}";

    public static string BuildResultDayCollectionRequestKey(string providerType, DateOnly raceDate)
        => $"{providerType}:result-day-collection:{raceDate:yyyy-MM-dd}";

    public static string BuildHistoricalRaceResultCollectionRequestKey(string providerType, DateOnly raceDate, string racecourse, int raceNumber)
        => $"{providerType}:historical-race-result:{raceDate:yyyy-MM-dd}:{DeterministicIdGenerator.NormalizeKey(racecourse)}:{raceNumber:D2}";

    public static string BuildHorseHistoryCollectionRequestKey(string providerType, string horseId)
        => $"{providerType}:horse-history:{horseId}";

    public static string BuildJockeyHistoryCollectionRequestKey(string providerType, string jockeyId)
        => $"{providerType}:jockey-history:{jockeyId}";

    public static string BuildTrainerProfileCollectionRequestKey(string providerType, string trainerId)
        => $"{providerType}:trainer-profile:{trainerId}";

    public static string BuildRaceCardCollectionKey(string providerType, DateOnly raceDate)
        => $"{providerType}:race-card:{raceDate:yyyy-MM-dd}";

    public static string BuildRaceResultCollectionKey(string providerType, DateOnly raceDate)
        => $"{providerType}:race-result:{raceDate:yyyy-MM-dd}";
}
