using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.MachineLearning.Features;

namespace HorseRacingPrediction.MachineLearning.Training;

/// <summary>
/// ReadModel のデータを ML.NET 入力特徴量に変換する静的ヘルパー。
/// </summary>
public static class FeatureMapper
{
    private static readonly Dictionary<string, float> RunningStyleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["逃"] = 1f,
        ["先"] = 2f,
        ["差"] = 3f,
        ["追"] = 4f,
    };

    private static readonly Dictionary<string, float> SexCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["M"] = 1f, ["牡"] = 1f,
        ["F"] = 2f, ["牝"] = 2f,
        ["G"] = 3f, ["騸"] = 3f,
    };

    private static readonly Dictionary<string, float> PaceTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SlowPace"] = 0f,
        ["MidPace"]  = 1f,
        ["HiPace"]   = 2f,
    };

    /// <summary>
    /// レース予測コンテキストと各種統計から1頭分の特徴量を生成する。
    /// </summary>
    public static HorseEntryFeatureInput Build(
        RacePredictionContextEntry entry,
        HorseRaceHistoryReadModel? horseHistory,
        JockeyRaceHistoryReadModel? jockeyHistory,
        int fieldLeaderCount,
        int fieldFrontRunnerCount,
        string favoredPaceType,
        float fieldSizeEffect,
        float expectedRacePosition,
        DateOnly raceDate = default,
        float finishPosition = 0f)
    {
        var surfaceCode  = horseHistory?.Entries.FirstOrDefault()?.SurfaceCode ?? string.Empty;
        var distance     = horseHistory?.Entries.FirstOrDefault()?.DistanceMeters ?? 0;
        var courseCode   = horseHistory?.Entries.FirstOrDefault()?.RacecourseCode ?? string.Empty;
        var dirCode      = horseHistory?.Entries.FirstOrDefault()?.DirectionCode ?? string.Empty;

        var latestJockeyId = horseHistory?.LatestJockeyId;
        var jockeyChanged  = (entry.JockeyId != null && latestJockeyId != null && entry.JockeyId != latestJockeyId) ? 1f : 0f;

        var daysFromLastRace = horseHistory is null
            ? 999f
            : (float)horseHistory.GetDaysFromLastRace(raceDate == default ? DateOnly.FromDateTime(DateTime.Today) : raceDate);

        return new HorseEntryFeatureInput
        {
            // Group A
            GateNumber          = entry.GateNumber ?? 0,
            AssignedWeight      = (float)(entry.AssignedWeight ?? 0m),
            Age                 = entry.Age ?? 0,
            DeclaredWeightDiff  = (float)(entry.DeclaredWeightDiff ?? 0m),
            DeclaredWeight      = (float)(entry.DeclaredWeight ?? 0m),
            RunningStyleCode    = RunningStyleMap.TryGetValue(entry.RunningStyleCode ?? string.Empty, out var rs) ? rs : 0f,
            SexCode             = SexCodeMap.TryGetValue(entry.SexCode ?? string.Empty, out var sc) ? sc : 0f,

            // Group B
            RecentAvgFinishPosition  = horseHistory is null ? 10f : (float)horseHistory.RecentAvgFinishPosition,
            HorseWinRate             = horseHistory is null ? 0f  : (float)horseHistory.WinRate,
            HorsePlaceRate           = horseHistory is null ? 0f  : (float)horseHistory.PlaceRate,
            HorseSurfaceWinRate      = horseHistory is null ? 0f  : (float)horseHistory.GetSurfaceWinRate(surfaceCode),
            DistanceSuitabilityScore = horseHistory is null ? 50f : (float)horseHistory.GetDistanceSuitabilityScore(distance),
            RacecourseSuitabilityScore = horseHistory is null ? 50f : (float)horseHistory.GetRacecourseSuitabilityScore(courseCode),
            DirectionSuitabilityScore  = horseHistory is null ? 50f : (float)horseHistory.GetDirectionSuitabilityScore(dirCode),
            WeightStabilityScore     = horseHistory is null ? 10f : (float)horseHistory.WeightStabilityScore,
            AvgLastThreeFurlongTime  = horseHistory is null ? 0f  : (float)horseHistory.AvgLastThreeFurlongTime,
            AvgPrizeMoney            = horseHistory is null ? 0f  : (float)(horseHistory.AvgPrizeMoney / 10000.0),
            AvgCornerPosition        = horseHistory is null ? 0f  : (float)horseHistory.GetAvgCornerPosition(),
            DaysFromLastRace         = daysFromLastRace,
            TotalRaceCount           = horseHistory is null ? 0f  : horseHistory.TotalRaceCount,

            // Group C
            JockeyRecentWinRate    = jockeyHistory is null ? 0f : (float)jockeyHistory.RecentWinRate,
            JockeyRecentPlaceRate  = jockeyHistory is null ? 0f : (float)jockeyHistory.RecentPlaceRate,
            JockeySurfaceWinRate   = jockeyHistory is null ? 0f : (float)jockeyHistory.GetSurfaceWinRate(surfaceCode),
            JockeyDistanceWinRate  = jockeyHistory is null ? 0f : (float)jockeyHistory.GetDistanceWinRate(distance),
            JockeyHorseComboCount  = jockeyHistory is null ? 0f : jockeyHistory.GetHorseComboCount(entry.HorseId),
            JockeyHorseComboWinRate = jockeyHistory is null ? 0f : (float)jockeyHistory.GetHorseComboWinRate(entry.HorseId),
            JockeyChanged          = jockeyChanged,

            // Group D
            FieldLeaderCount      = fieldLeaderCount,
            FieldFrontRunnerCount = fieldFrontRunnerCount,
            ExpectedRacePosition  = expectedRacePosition,
            FavoredPaceType       = PaceTypeMap.TryGetValue(favoredPaceType, out var pt) ? pt : 1f,
            FieldSizeEffect       = fieldSizeEffect,

            FinishPosition = finishPosition,
        };
    }
}
