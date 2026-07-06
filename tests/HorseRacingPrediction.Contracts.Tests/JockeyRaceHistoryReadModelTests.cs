using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class JockeyRaceHistoryReadModelTests
{
    [TestMethod]
    public void TotalRaceCount_ReturnsEntryCount()
    {
        var model = CreateModel(
            CreateEntry(index: 1),
            CreateEntry(index: 2));

        Assert.AreEqual(2, model.TotalRaceCount);
    }

    [TestMethod]
    public void WinRate_NoEntries_ReturnsZero()
    {
        var model = CreateModel();

        Assert.AreEqual(0d, model.WinRate);
    }

    [TestMethod]
    public void WinRate_ComputesRatioOfFirstPlaceFinishes()
    {
        var model = CreateModel(
            CreateEntry(finishPosition: 1, index: 1),
            CreateEntry(finishPosition: 4, index: 2),
            CreateEntry(finishPosition: 1, index: 3),
            CreateEntry(finishPosition: 1, index: 4));

        Assert.AreEqual(0.75d, model.WinRate, 1e-9);
    }

    [TestMethod]
    public void PlaceRate_ComputesRatioOfTopThreeFinishes()
    {
        var model = CreateModel(
            CreateEntry(finishPosition: 2, index: 1),
            CreateEntry(finishPosition: 3, index: 2),
            CreateEntry(finishPosition: 8, index: 3));

        Assert.AreEqual(2d / 3d, model.PlaceRate, 1e-9);
    }

    [TestMethod]
    public void RecentWinRate_LimitsToTwentyMostRecentEntriesByRaceDate()
    {
        var entries = new List<JockeyRaceHistoryEntry>();
        for (var i = 0; i < 20; i++)
        {
            entries.Add(CreateEntry(raceDate: new DateOnly(2026, 1, 1).AddDays(i), finishPosition: 1, index: i));
        }

        // 20走より前の負け続きを1件追加。直近20走のWinRateには影響しない。
        entries.Insert(0, CreateEntry(raceDate: new DateOnly(2025, 1, 1), finishPosition: 10, index: 99));

        var model = CreateModel(entries.ToArray());

        Assert.AreEqual(1.0d, model.RecentWinRate, 1e-9);
        Assert.AreEqual(20d / 21d, model.WinRate, 1e-9);
    }

    [TestMethod]
    public void GetSurfaceWinRate_FiltersBySurfaceCode()
    {
        var model = CreateModel(
            CreateEntry(surfaceCode: "芝", finishPosition: 1, index: 1),
            CreateEntry(surfaceCode: "ダート", finishPosition: 1, index: 2),
            CreateEntry(surfaceCode: "ダート", finishPosition: 5, index: 3));

        Assert.AreEqual(0.5d, model.GetSurfaceWinRate("ダート"), 1e-9);
    }

    [TestMethod]
    public void GetDistanceWinRate_FiltersEntriesWithin200MeterTolerance()
    {
        var model = CreateModel(
            CreateEntry(distanceMeters: 2000, finishPosition: 1, index: 1),
            CreateEntry(distanceMeters: 2100, finishPosition: 5, index: 2),
            CreateEntry(distanceMeters: 3000, finishPosition: 1, index: 3));

        Assert.AreEqual(0.5d, model.GetDistanceWinRate(2000), 1e-9);
    }

    [TestMethod]
    public void GetHorseComboCount_CountsEntriesForSpecificHorse()
    {
        var model = CreateModel(
            CreateEntry(horseId: "horse-a", index: 1),
            CreateEntry(horseId: "horse-a", index: 2),
            CreateEntry(horseId: "horse-b", index: 3));

        Assert.AreEqual(2, model.GetHorseComboCount("horse-a"));
    }

    [TestMethod]
    public void GetHorseComboWinRate_ComputesRatioForSpecificHorse()
    {
        var model = CreateModel(
            CreateEntry(horseId: "horse-a", finishPosition: 1, index: 1),
            CreateEntry(horseId: "horse-a", finishPosition: 2, index: 2),
            CreateEntry(horseId: "horse-b", finishPosition: 1, index: 3));

        Assert.AreEqual(0.5d, model.GetHorseComboWinRate("horse-a"), 1e-9);
    }

    [TestMethod]
    public void GetHorseComboWinRate_NoMatchingHorse_ReturnsZero()
    {
        var model = CreateModel(CreateEntry(horseId: "horse-a", finishPosition: 1, index: 1));

        Assert.AreEqual(0d, model.GetHorseComboWinRate("horse-unknown"));
    }

    private static JockeyRaceHistoryReadModel CreateModel(params JockeyRaceHistoryEntry[] entries)
        => new() { JockeyId = "jockey-1", Entries = entries.ToList() };

    private static JockeyRaceHistoryEntry CreateEntry(
        DateOnly? raceDate = null,
        string? surfaceCode = null,
        int? distanceMeters = null,
        int? finishPosition = null,
        string horseId = "horse-default",
        int index = 0)
        => new(
            RaceId: $"race-{index}",
            EntryId: $"entry-{index}",
            HorseId: horseId,
            RaceDate: raceDate,
            RacecourseCode: null,
            SurfaceCode: surfaceCode,
            DistanceMeters: distanceMeters,
            DirectionCode: null,
            GradeCode: null,
            FinishPosition: finishPosition,
            PrizeMoney: null);
}
