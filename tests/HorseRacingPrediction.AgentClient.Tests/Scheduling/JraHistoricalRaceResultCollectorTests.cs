using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.ApiClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class JraHistoricalRaceResultCollectorTests
{
    [TestMethod]
    public async Task CollectAsync_WhenResultIsAvailable_SavesRaceAndEntryResults()
    {
        var lookup = new StubJraRaceResultLookup
        {
            Result = new JraExtractionEnvelope<JraRaceResultSummary>(
                true,
                JraPageKind.Result,
                "https://example.test/result",
                new JraNavigationTrace(Array.Empty<string>(), TimeSpan.Zero),
                new JraRaceResultSummary(
                    "皐月賞",
                    new DateOnly(2026, 4, 13),
                    "中山",
                    11,
                    "GⅠ",
                    "芝",
                    2000,
                    "右",
                    [
                        new JraResultEntry(1, 3, 2, "ソールオリエンス", "横山武史", "1:57.8", 56.0m, "牡3", 480.0m, 4.0m),
                        new JraResultEntry(2, 8, 4, "タスティエーラ", "松山弘平", "1:58.0", 57.0m, "せん4", 500.0m, -2.0m),
                    ],
                    Array.Empty<JraPayoutSummary>(),
                    "https://example.test/result"))
        };
        var writeService = new StubDataCollectionWriteService();
        var stateStore = new ProcessingStateStore(
            Options.Create(new AgentProcessingOptions
            {
                StateDirectory = Path.Combine(Path.GetTempPath(), "jra-historical-race-result-collector-tests", Guid.NewGuid().ToString("N")),
                PredictionLeaseMinutes = 5,
                CollectionLeaseMinutes = 5,
            }),
            NullLogger<ProcessingStateStore>.Instance);
        var sut = new JraHistoricalRaceResultCollector(lookup, new DataCollectionWriteTools(writeService), stateStore);

        var result = await sut.CollectAsync(
            new HistoricalRaceResultCollectionRequestPayload(
                new DateOnly(2026, 4, 13),
                "中山",
                11,
                "race-1",
                "JRA"));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("皐月賞", writeService.UpsertedRaceName);
        Assert.HasCount(2, writeService.UpsertedEntries);
        Assert.AreEqual((3, "ソールオリエンス", "横山武史", 56.0m, "M", 3), writeService.UpsertedEntries[0]);
        Assert.AreEqual((8, "タスティエーラ", "松山弘平", 57.0m, "G", 4), writeService.UpsertedEntries[1]);
        Assert.HasCount(2, writeService.RecordedEntryResults);
        Assert.AreEqual("ソールオリエンス", writeService.WinningHorseName);
        Assert.AreEqual("GⅠ", writeService.UpsertedGradeCode);
        Assert.AreEqual("芝", writeService.UpsertedSurfaceCode);
        Assert.AreEqual(2000, writeService.UpsertedDistanceMeters);
        Assert.AreEqual("右", writeService.UpsertedDirectionCode);
    }

    [TestMethod]
    public async Task CollectAsync_AllowsEntriesWithoutJockeyName()
    {
        var lookup = new StubJraRaceResultLookup
        {
            Result = new JraExtractionEnvelope<JraRaceResultSummary>(
                true,
                JraPageKind.Result,
                "https://example.test/result",
                new JraNavigationTrace(Array.Empty<string>(), TimeSpan.Zero),
                new JraRaceResultSummary(
                    "テストレース",
                    new DateOnly(2026, 5, 2),
                    "新潟",
                    12,
                    "1勝クラス",
                    "芝",
                    1000,
                    null,
                    [
                        new JraResultEntry(1, 1, 1, "有効馬", "騎手A", "0:56.1", 56.0m, "牝6", 450.0m, -10.0m),
                        new JraResultEntry(2, 2, 1, "無効馬", null, "0:56.4", 56.0m, "牝4", 430.0m, 0.0m),
                    ],
                    Array.Empty<JraPayoutSummary>(),
                    "https://example.test/result"))
        };
        var writeService = new StubDataCollectionWriteService();
        var stateStore = new ProcessingStateStore(
            Options.Create(new AgentProcessingOptions
            {
                StateDirectory = Path.Combine(Path.GetTempPath(), "jra-historical-race-result-collector-tests", Guid.NewGuid().ToString("N")),
                PredictionLeaseMinutes = 5,
                CollectionLeaseMinutes = 5,
            }),
            NullLogger<ProcessingStateStore>.Instance);
        var sut = new JraHistoricalRaceResultCollector(lookup, new DataCollectionWriteTools(writeService), stateStore);

        var result = await sut.CollectAsync(
            new HistoricalRaceResultCollectionRequestPayload(
                new DateOnly(2026, 5, 2),
                "新潟",
                12,
                "race-1",
                "JRA"));

        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(2, writeService.UpsertedEntries);
        Assert.AreEqual("有効馬", writeService.UpsertedEntries[0].HorseName);
        Assert.IsNull(writeService.UpsertedEntries[1].JockeyName);
        Assert.HasCount(2, writeService.RecordedEntryResults);
    }

    private sealed class StubJraRaceResultLookup : IJraRaceResultLookup
    {
        public JraExtractionEnvelope<JraRaceResultSummary>? Result { get; set; }

        public Task<JraExtractionEnvelope<JraRaceResultSummary>> GetRaceResultAsync(DateOnly raceDate, string racecourse, int raceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(Result!);
    }

    private sealed class StubDataCollectionWriteService : IDataCollectionWriteService
    {
        public List<(int HorseNumber, int? FinishPosition, string? OfficialTime)> RecordedEntryResults { get; } = [];

        public List<(int HorseNumber, string HorseName, string? JockeyName, decimal? AssignedWeight, string? SexCode, int? Age)> UpsertedEntries { get; } = [];

        public string? UpsertedRaceName { get; private set; }
        public string? UpsertedGradeCode { get; private set; }
        public string? UpsertedSurfaceCode { get; private set; }
        public int? UpsertedDistanceMeters { get; private set; }
        public string? UpsertedDirectionCode { get; private set; }

        public string? WinningHorseName { get; private set; }

        public Task<string> UpsertRaceAsync(string raceDate, string racecourseCode, int raceNumber, string raceName, int? entryCount, string? gradeCode, string? surfaceCode, int? distanceMeters, string? directionCode, CancellationToken cancellationToken = default)
        {
            UpsertedRaceName = raceName;
            UpsertedGradeCode = gradeCode;
            UpsertedSurfaceCode = surfaceCode;
            UpsertedDistanceMeters = distanceMeters;
            UpsertedDirectionCode = directionCode;
            return Task.FromResult("race-1");
        }

        public Task<string> DeclareRaceResultAsync(string raceId, string winningHorseName, string? declaredAt, string? winningHorseId, CancellationToken cancellationToken = default)
        {
            WinningHorseName = winningHorseName;
            return Task.FromResult("ok");
        }

        public Task<string> DeclareRaceEntryResultAsync(string raceId, int horseNumber, int? finishPosition, string? officialTime, string? marginText, string? lastThreeFurlongTime, string? abnormalResultCode, decimal? prizeMoney, CancellationToken cancellationToken = default)
        {
            RecordedEntryResults.Add((horseNumber, finishPosition, officialTime));
            return Task.FromResult("ok");
        }

        public Task<string> UpsertHorseAsync(string registeredName, string? normalizedName, string? sexCode, string? birthDate, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> UpsertJockeyAsync(string displayName, string? normalizedName, string? affiliationCode, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> UpsertTrainerAsync(string displayName, string? normalizedName, string? affiliationCode, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> UpsertRaceEntryAsync(string raceId, int horseNumber, string horseName, string? jockeyName, string? trainerName, int? gateNumber, decimal? assignedWeight, string? sexCode, int? age, decimal? declaredWeight, decimal? declaredWeightDiff, CancellationToken cancellationToken = default)
        {
            UpsertedEntries.Add((horseNumber, horseName, jockeyName, assignedWeight, sexCode, age));
            return Task.FromResult("ok");
        }

        public Task<string> DeclareRacePayoutsAsync(string raceId, string? winPayoutsJson, string? placePayoutsJson, string? quinellaPayoutsJson, string? exactaPayoutsJson, string? trifectaPayoutsJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}