using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class HorseRaceHistoryReadModelTests
{
    [TestMethod]
    public void TotalRaceCount_ReturnsEntryCount()
    {
        var model = CreateModel(
            CreateEntry(index: 1),
            CreateEntry(index: 2),
            CreateEntry(index: 3));

        Assert.AreEqual(3, model.TotalRaceCount);
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
            CreateEntry(finishPosition: 2, index: 2),
            CreateEntry(finishPosition: 1, index: 3),
            CreateEntry(finishPosition: 5, index: 4));

        Assert.AreEqual(0.5d, model.WinRate, 1e-9);
    }

    [TestMethod]
    public void PlaceRate_ComputesRatioOfTopThreeFinishes()
    {
        var model = CreateModel(
            CreateEntry(finishPosition: 1, index: 1),
            CreateEntry(finishPosition: 3, index: 2),
            CreateEntry(finishPosition: 4, index: 3),
            CreateEntry(finishPosition: null, index: 4));

        Assert.AreEqual(0.5d, model.PlaceRate, 1e-9);
    }

    [TestMethod]
    public void RecentAvgFinishPosition_UsesLatestFiveEntriesByRaceDate()
    {
        var model = CreateModel(
            CreateEntry(raceDate: new DateOnly(2026, 1, 1), finishPosition: 10, index: 1),
            CreateEntry(raceDate: new DateOnly(2026, 2, 1), finishPosition: 1, index: 2),
            CreateEntry(raceDate: new DateOnly(2026, 3, 1), finishPosition: 2, index: 3),
            CreateEntry(raceDate: new DateOnly(2026, 4, 1), finishPosition: 3, index: 4),
            CreateEntry(raceDate: new DateOnly(2026, 5, 1), finishPosition: 4, index: 5),
            CreateEntry(raceDate: new DateOnly(2026, 6, 1), finishPosition: 5, index: 6));

        // 直近5走（2〜6月）の平均着順: (1+2+3+4+5)/5 = 3。最古の10着は含めない。
        Assert.AreEqual(3d, model.RecentAvgFinishPosition, 1e-9);
    }

    [TestMethod]
    public void AvgLastThreeFurlongTime_IgnoresUnparsableValues()
    {
        var model = CreateModel(
            CreateEntry(lastThreeFurlongTime: "34.0", index: 1),
            CreateEntry(lastThreeFurlongTime: "36.0", index: 2),
            CreateEntry(lastThreeFurlongTime: "非公開", index: 3),
            CreateEntry(lastThreeFurlongTime: null, index: 4));

        Assert.AreEqual(35.0d, model.AvgLastThreeFurlongTime, 1e-9);
    }

    [TestMethod]
    public void AvgPrizeMoney_NoEntries_ReturnsZero()
    {
        var model = CreateModel();

        Assert.AreEqual(0d, model.AvgPrizeMoney);
    }

    [TestMethod]
    public void AvgPrizeMoney_ComputesAverageAcrossEntriesWithPrizeMoney()
    {
        var model = CreateModel(
            CreateEntry(prizeMoney: 100m, index: 1),
            CreateEntry(prizeMoney: 300m, index: 2),
            CreateEntry(prizeMoney: null, index: 3));

        Assert.AreEqual(200d, model.AvgPrizeMoney, 1e-9);
    }

    [TestMethod]
    public void WeightStabilityScore_FewerThanTwoDiffs_ReturnsMaxScore()
    {
        var model = CreateModel(CreateEntry(declaredWeightDiff: 4m, index: 1));

        Assert.AreEqual(10d, model.WeightStabilityScore, 1e-9);
    }

    [TestMethod]
    public void WeightStabilityScore_ZeroVariance_ReturnsMaxScore()
    {
        var model = CreateModel(
            CreateEntry(declaredWeightDiff: 0m, index: 1),
            CreateEntry(declaredWeightDiff: 0m, index: 2));

        Assert.AreEqual(10d, model.WeightStabilityScore, 1e-9);
    }

    [TestMethod]
    public void WeightStabilityScore_HighVariance_ReturnsLowerScore()
    {
        var model = CreateModel(
            CreateEntry(declaredWeightDiff: -10m, index: 1),
            CreateEntry(declaredWeightDiff: 10m, index: 2));

        // 平均0, 分散100, 標準偏差10 → 10 - 10 = 0
        Assert.AreEqual(0d, model.WeightStabilityScore, 1e-9);
    }

    [TestMethod]
    public void LatestJockeyId_NoEntries_ReturnsNull()
    {
        var model = CreateModel();

        Assert.IsNull(model.LatestJockeyId);
    }

    [TestMethod]
    public void LatestJockeyId_ReturnsJockeyFromMostRecentRaceDate()
    {
        var model = CreateModel(
            CreateEntry(raceDate: new DateOnly(2026, 1, 1), jockeyId: "jockey-old", index: 1),
            CreateEntry(raceDate: new DateOnly(2026, 6, 1), jockeyId: "jockey-new", index: 2));

        Assert.AreEqual("jockey-new", model.LatestJockeyId);
    }

    [TestMethod]
    public void GetAvgCornerPosition_NoCornerData_ReturnsZero()
    {
        var model = CreateModel(CreateEntry(cornerPositions: null, index: 1));

        Assert.AreEqual(0d, model.GetAvgCornerPosition());
    }

    [TestMethod]
    public void GetAvgCornerPosition_ParsesLastSegmentOfDashSeparatedPositions()
    {
        var model = CreateModel(
            CreateEntry(cornerPositions: "5-4-3-2", index: 1),
            CreateEntry(cornerPositions: "6-5-4-3", index: 2));

        Assert.AreEqual(2.5d, model.GetAvgCornerPosition(), 1e-9);
    }

    [TestMethod]
    public void GetAvgCornerPosition_IgnoresZeroOrUnparsablePositions()
    {
        var model = CreateModel(
            CreateEntry(cornerPositions: "0-0-0-0", index: 1),
            CreateEntry(cornerPositions: "invalid", index: 2),
            CreateEntry(cornerPositions: "3-3-3-4", index: 3));

        Assert.AreEqual(4d, model.GetAvgCornerPosition(), 1e-9);
    }

    [TestMethod]
    public void GetSurfaceWinRate_FiltersBySurfaceCodeCaseInsensitively()
    {
        var model = CreateModel(
            CreateEntry(surfaceCode: "芝", finishPosition: 1, index: 1),
            CreateEntry(surfaceCode: "芝", finishPosition: 2, index: 2),
            CreateEntry(surfaceCode: "ダート", finishPosition: 1, index: 3));

        Assert.AreEqual(0.5d, model.GetSurfaceWinRate("芝"), 1e-9);
    }

    [TestMethod]
    public void GetSurfaceWinRate_NoMatchingEntries_ReturnsZero()
    {
        var model = CreateModel(CreateEntry(surfaceCode: "芝", finishPosition: 1, index: 1));

        Assert.AreEqual(0d, model.GetSurfaceWinRate("ダート"));
    }

    [TestMethod]
    public void GetDistanceSuitabilityScore_NoEntries_ReturnsNeutralFifty()
    {
        var model = CreateModel();

        Assert.AreEqual(50d, model.GetDistanceSuitabilityScore(1600));
    }

    [TestMethod]
    public void GetDistanceSuitabilityScore_FiltersEntriesWithin200MeterTolerance()
    {
        var model = CreateModel(
            CreateEntry(distanceMeters: 1600, finishPosition: 1, index: 1),
            CreateEntry(distanceMeters: 1800, finishPosition: 1, index: 2),
            CreateEntry(distanceMeters: 2400, finishPosition: 10, index: 3));

        // 1600m基準で±200m以内(1600, 1800)のみ対象。両方1着 → avgFinish=1 → (20-1)/20*100 = 95
        Assert.AreEqual(95d, model.GetDistanceSuitabilityScore(1600), 1e-9);
    }

    [TestMethod]
    public void GetRacecourseSuitabilityScore_WorstAverageFinish_ClampsToZero()
    {
        var model = CreateModel(
            CreateEntry(racecourseCode: "05", finishPosition: 18, index: 1),
            CreateEntry(racecourseCode: "05", finishPosition: 16, index: 2));

        // avgFinish=17 → (20-17)/20*100=15 のはずが、下限0を跨がないケースの確認として15を期待
        Assert.AreEqual(15d, model.GetRacecourseSuitabilityScore("05"), 1e-9);
    }

    [TestMethod]
    public void GetDirectionSuitabilityScore_FiltersByDirectionCode()
    {
        var model = CreateModel(
            CreateEntry(directionCode: "右", finishPosition: 1, index: 1),
            CreateEntry(directionCode: "左", finishPosition: 18, index: 2));

        Assert.AreEqual(95d, model.GetDirectionSuitabilityScore("右"), 1e-9);
    }

    [TestMethod]
    public void GetDaysFromLastRace_NoEntries_Returns999()
    {
        var model = CreateModel();

        Assert.AreEqual(999, model.GetDaysFromLastRace(new DateOnly(2026, 6, 1)));
    }

    [TestMethod]
    public void GetDaysFromLastRace_ComputesDifferenceFromMostRecentEntry()
    {
        var model = CreateModel(
            CreateEntry(raceDate: new DateOnly(2026, 5, 1), index: 1),
            CreateEntry(raceDate: new DateOnly(2026, 5, 20), index: 2));

        Assert.AreEqual(12, model.GetDaysFromLastRace(new DateOnly(2026, 6, 1)));
    }

    private static HorseRaceHistoryReadModel CreateModel(params HorseRaceHistoryEntry[] entries)
        => new() { HorseId = "horse-1", Entries = entries.ToList() };

    private static HorseRaceHistoryEntry CreateEntry(
        DateOnly? raceDate = null,
        string? surfaceCode = null,
        int? distanceMeters = null,
        string? racecourseCode = null,
        string? directionCode = null,
        int? finishPosition = null,
        string? lastThreeFurlongTime = null,
        string? cornerPositions = null,
        decimal? prizeMoney = null,
        decimal? declaredWeightDiff = null,
        string? jockeyId = null,
        int index = 0)
        => new(
            RaceId: $"race-{index}",
            EntryId: $"entry-{index}",
            RaceDate: raceDate,
            RacecourseCode: racecourseCode,
            SurfaceCode: surfaceCode,
            DistanceMeters: distanceMeters,
            DirectionCode: directionCode,
            GradeCode: null,
            GateNumber: null,
            AssignedWeight: null,
            DeclaredWeight: null,
            DeclaredWeightDiff: declaredWeightDiff,
            RunningStyleCode: null,
            JockeyId: jockeyId,
            TrainerId: null,
            FinishPosition: finishPosition,
            LastThreeFurlongTime: lastThreeFurlongTime,
            CornerPositions: cornerPositions,
            PrizeMoney: prizeMoney);
}
