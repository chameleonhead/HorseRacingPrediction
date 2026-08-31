using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Collector.Scheduling;

public static class RaceDataCollectionKeyFactory
{
    public static string Build(DateOnly raceDate, string racecourse, int raceNumber)
        => $"{raceDate:yyyy-MM-dd}|{DeterministicIdGenerator.NormalizeKey(racecourse)}|{raceNumber:D2}";
}