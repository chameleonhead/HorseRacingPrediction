using HorseRacingPrediction.Application.Queries.ReadModels;

namespace HorseRacingPrediction.MachineLearning.Tests;

/// <summary>
/// テスト用 ReadModel ヘルパー
/// </summary>
internal static class ReadModelTestHelpers
{
    public static void SetTestData(this HorseRaceHistoryReadModel model, string horseId)
    {
        SetProperty(model, nameof(HorseRaceHistoryReadModel.HorseId), horseId);
    }

    /// <summary>LatestJockeyId フィールドをリフレクションで直接設定する（テスト用）</summary>
    public static void SetLatestJockeyId(this HorseRaceHistoryReadModel model, string jockeyId)
    {
        model.Entries.Add(new HorseRaceHistoryEntry(
            "race-stub", "entry-stub", null, null, null, null, null, null,
            null, null, null, null, null, jockeyId, null,
            null, null, null, null));
    }

    /// <summary>履歴エントリーを追加する（勝率計算のため）</summary>
    public static void AddHistoryEntry(
        this HorseRaceHistoryReadModel model, string horseId, int finishPosition)
    {
        model.Entries.Add(new HorseRaceHistoryEntry(
            $"race-{Guid.NewGuid()}", $"entry-{Guid.NewGuid()}",
            DateOnly.Parse("2024-01-01"), null, null, null, null, null,
            null, null, null, null, null, null, null,
            finishPosition, null, null, null));
    }

    public static void SetTestData(
        this RacePredictionContextReadModel model,
        string raceId, DateOnly raceDate, string racecourseCode, int raceNumber, string raceName)
    {
        SetProperty(model, nameof(RacePredictionContextReadModel.RaceId), raceId);
        SetProperty(model, nameof(RacePredictionContextReadModel.RaceDate), raceDate);
        SetProperty(model, nameof(RacePredictionContextReadModel.RacecourseCode), racecourseCode);
        SetProperty(model, nameof(RacePredictionContextReadModel.RaceNumber), raceNumber);
        SetProperty(model, nameof(RacePredictionContextReadModel.RaceName), raceName);
    }

    public static void AddEntry(this RacePredictionContextReadModel model, RacePredictionContextEntry entry)
    {
        model.Entries.Add(entry);
    }

    public static void SetTestData(this RaceResultViewReadModel model, string raceId)
    {
        SetProperty(model, nameof(RaceResultViewReadModel.RaceId), raceId);
    }

    public static void AddEntryResult(this RaceResultViewReadModel model, EntryResultSnapshot result)
    {
        model.EntryResults.Add(result);

        var index = model.EntryIndexes.FindIndex(x => x.EntryId == result.EntryId);
        var snapshot = new RaceEntryIndexSnapshot(result.EntryId, result.HorseId, result.HorseNumber);
        if (index >= 0)
        {
            model.EntryIndexes[index] = snapshot;
        }
        else
        {
            model.EntryIndexes.Add(snapshot);
        }
    }

    private static void SetProperty(object obj, string propertyName, object? value)
    {
        obj.GetType().GetProperty(propertyName)!.SetValue(obj, value);
    }
}
