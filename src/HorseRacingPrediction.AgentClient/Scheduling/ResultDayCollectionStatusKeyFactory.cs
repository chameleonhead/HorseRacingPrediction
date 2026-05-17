namespace HorseRacingPrediction.AgentClient.Scheduling;

public static class ResultDayCollectionStatusKeyFactory
{
    public static string Build(string providerType, DateOnly targetDate)
        => $"{providerType}:result-day:{targetDate:yyyy-MM-dd}";
}