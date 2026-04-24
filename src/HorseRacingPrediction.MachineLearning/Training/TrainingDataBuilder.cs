using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.MachineLearning.Features;

namespace HorseRacingPrediction.MachineLearning.Training;

/// <summary>
/// 過去レースの結果 ReadModel から ML.NET 訓練データを生成する。
/// </summary>
public static class TrainingDataBuilder
{
    /// <summary>
    /// 過去レースの結果から訓練サンプルを生成する（非同期版）。
    /// </summary>
    public static async Task<List<HorseEntryFeatureInput>> BuildAsync(
        IEnumerable<RaceResultViewReadModel> raceResults,
        Func<string, CancellationToken, Task<RacePredictionContextReadModel?>> getRaceContext,
        Func<string, CancellationToken, Task<HorseRaceHistoryReadModel?>> getHorseHistory,
        Func<string, CancellationToken, Task<JockeyRaceHistoryReadModel?>> getJockeyHistory,
        CancellationToken cancellationToken = default)
    {
        var result = new List<HorseEntryFeatureInput>();

        foreach (var race in raceResults)
        {
            if (race.EntryResults.Count == 0) continue;

            var context = await getRaceContext(race.RaceId, cancellationToken).ConfigureAwait(false);
            if (context is null) continue;

            var entries = context.Entries;
            var fieldSize = entries.Count;
            var raceDate = race.RaceDate ?? DateOnly.FromDateTime(DateTime.Today);

            var leaderCount      = entries.Count(e => e.RunningStyleCode == "逃");
            var frontRunnerCount = entries.Count(e => e.RunningStyleCode == "先");
            var paceType = leaderCount >= 3 ? "HiPace" : leaderCount == 2 ? "MidPace" : "SlowPace";
            var fieldSizeEffect  = Math.Min(100f, (fieldSize - 6) * 100f / 12f);

            foreach (var entryResult in race.EntryResults)
            {
                if (!entryResult.FinishPosition.HasValue) continue;

                var entry = entries.FirstOrDefault(e => e.EntryId == entryResult.EntryId);
                if (entry is null) continue;

                var horseHistory  = await getHorseHistory(entry.HorseId, cancellationToken).ConfigureAwait(false);
                var jockeyHistory = entry.JockeyId is null ? null
                    : await getJockeyHistory(entry.JockeyId, cancellationToken).ConfigureAwait(false);

                var expectedPos = EstimatePosition(entry.RunningStyleCode, leaderCount, frontRunnerCount);

                result.Add(FeatureMapper.Build(
                    entry,
                    horseHistory,
                    jockeyHistory,
                    leaderCount,
                    frontRunnerCount,
                    paceType,
                    fieldSizeEffect,
                    expectedPos,
                    raceDate,
                    finishPosition: entryResult.FinishPosition.Value));
            }
        }

        return result;
    }

    private static float EstimatePosition(string? runningStyleCode, int leaderCount, int frontRunnerCount)
    {
        return runningStyleCode switch
        {
            "逃" => 1.5f,
            "先" => 1.5f + leaderCount + (frontRunnerCount / 2f),
            "差" => 1.5f + leaderCount + frontRunnerCount + 2f,
            "追" => 1.5f + leaderCount + frontRunnerCount + 5f,
            _    => 8f,
        };
    }
}
