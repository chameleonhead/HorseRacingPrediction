// JRAサイト再設計（docs/jra-scraping.md）により、対象の JraHistoricalDataRequestHandler は一時的に無効化されている。
#if false
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.ApiClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.Scraping.Jra;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class JraHistoricalDataRequestHandlerTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "jra-historical-data-request-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task HandleHorseHistoryRequestAsync_SynchronizesProfileAndPersistsRaceHistory()
    {
        var raceQueryService = new StubRaceQueryService
        {
            Horse = new HorseReadModel
            {
                HorseId = "horse-1",
                RegisteredName = "ソールオリエンス",
                NormalizedName = "ソールオリエンス",
            },
        };
        var writeService = new StubDataCollectionWriteService();
        var horseProfile = new JraEntityProfile(
            "horse",
            "ソールオリエンス",
            "牡",
            new DateOnly(2020, 2, 24),
            null,
            null,
            "キタサンブラック",
            "スキア",
            "社台レースホース",
            "社台ファーム",
            "手塚貴久",
            new Dictionary<string, string>(),
            "https://example.test/horse");
        var raceHistory = new List<JraHorseRaceHistoryEntryData>
        {
            new(
                RaceDate: new DateOnly(2023, 5, 28),
                Racecourse: "東京",
                RaceNumber: 11,
                RaceName: "日本ダービー",
                GateNumber: 1,
                HorseNumber: 1,
                FinishPosition: 1,
                AbnormalResultCode: null,
                JockeyName: "横山武史",
                AssignedWeight: 57.0m,
                SurfaceCode: "芝",
                DistanceMeters: 2400,
                OfficialTime: "2:22.5",
                MarginText: null,
                LastThreeFurlongTime: "33.8",
                BodyWeight: 468m,
                BodyWeightDiff: 2m,
                WinnerOrRunnerUpHorseName: "タスティエーラ",
                PrizeMoney: 20000m),
            new(
                RaceDate: new DateOnly(2023, 10, 29),
                Racecourse: "東京",
                RaceNumber: 11,
                RaceName: "天皇賞(秋)",
                GateNumber: 3,
                HorseNumber: 6,
                FinishPosition: 2,
                AbnormalResultCode: null,
                JockeyName: "横山武史",
                AssignedWeight: 58.0m,
                SurfaceCode: "芝",
                DistanceMeters: 2000,
                OfficialTime: "1:56.9",
                MarginText: "クビ",
                LastThreeFurlongTime: "33.5",
                BodyWeight: 470m,
                BodyWeightDiff: 2m,
                WinnerOrRunnerUpHorseName: "イクイノックス",
                PrizeMoney: 9000m),
        };
        var profileLookup = new StubJraProfileLookup
        {
            HorseProfileWithHistory = new JraExtractionEnvelope<JraHorseProfileData>(
                true,
                JraPageKind.HorseProfile,
                "https://example.test/horse",
                new JraNavigationTrace(Array.Empty<string>(), TimeSpan.Zero),
                new JraHorseProfileData(horseProfile, raceHistory)),
        };
        var sut = new JraHistoricalDataRequestHandler(raceQueryService, writeService, profileLookup, CreateStatusRecorder());

        var result = await sut.HandleHorseHistoryRequestAsync(
            new HorseHistoryCollectionRequestPayload("horse-1", "race-1", "JRA"));

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsFalse(result.IsPermanentFailure);
        Assert.AreEqual("ソールオリエンス", writeService.UpsertedHorseName);
        Assert.AreEqual("社台レースホース", writeService.UpsertedHorseOwnerName);
        Assert.AreEqual("牡", writeService.UpsertedHorseSexCode);
        Assert.AreEqual("2020-02-24", writeService.UpsertedHorseBirthDate);
        Assert.IsTrue(result.Message?.Contains("RaceHistoryEntriesPersisted=2/2", StringComparison.Ordinal));

        Assert.HasCount(2, writeService.UpsertedRaces);
        Assert.AreEqual("日本ダービー", writeService.UpsertedRaces[0].RaceName);
        Assert.AreEqual("東京", writeService.UpsertedRaces[0].RacecourseCode);
        Assert.AreEqual(11, writeService.UpsertedRaces[0].RaceNumber);

        Assert.HasCount(2, writeService.UpsertedRaceEntries);
        Assert.AreEqual("ソールオリエンス", writeService.UpsertedRaceEntries[0].HorseName);
        Assert.AreEqual(1, writeService.UpsertedRaceEntries[0].HorseNumber);

        Assert.HasCount(2, writeService.DeclaredRaceResults);
        Assert.AreEqual("ソールオリエンス", writeService.DeclaredRaceResults[0].WinningHorseName, "自身が勝った場合は自身の馬名");
        Assert.AreEqual("イクイノックス", writeService.DeclaredRaceResults[1].WinningHorseName, "自身が負けた場合は勝ち馬の馬名");

        Assert.HasCount(2, writeService.DeclaredRaceEntryResults);
        Assert.AreEqual(1, writeService.DeclaredRaceEntryResults[0].FinishPosition);
        Assert.AreEqual(2, writeService.DeclaredRaceEntryResults[1].FinishPosition);
    }

    [TestMethod]
    public async Task HandleHorseHistoryRequestAsync_SkipsRaceHistoryEntriesMissingRequiredFields()
    {
        var raceQueryService = new StubRaceQueryService
        {
            Horse = new HorseReadModel
            {
                HorseId = "horse-1",
                RegisteredName = "ソールオリエンス",
                NormalizedName = "ソールオリエンス",
            },
        };
        var writeService = new StubDataCollectionWriteService();
        var horseProfile = new JraEntityProfile(
            "horse",
            "ソールオリエンス",
            "牡",
            new DateOnly(2020, 2, 24),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>(),
            "https://example.test/horse");
        var raceHistory = new List<JraHorseRaceHistoryEntryData>
        {
            new(
                RaceDate: null,
                Racecourse: "東京",
                RaceNumber: 11,
                RaceName: "馬番不明レース",
                GateNumber: null,
                HorseNumber: null,
                FinishPosition: null,
                AbnormalResultCode: null,
                JockeyName: null,
                AssignedWeight: null,
                SurfaceCode: null,
                DistanceMeters: null,
                OfficialTime: null,
                MarginText: null,
                LastThreeFurlongTime: null,
                BodyWeight: null,
                BodyWeightDiff: null,
                WinnerOrRunnerUpHorseName: null,
                PrizeMoney: null),
        };
        var profileLookup = new StubJraProfileLookup
        {
            HorseProfileWithHistory = new JraExtractionEnvelope<JraHorseProfileData>(
                true,
                JraPageKind.HorseProfile,
                "https://example.test/horse",
                new JraNavigationTrace(Array.Empty<string>(), TimeSpan.Zero),
                new JraHorseProfileData(horseProfile, raceHistory)),
        };
        var sut = new JraHistoricalDataRequestHandler(raceQueryService, writeService, profileLookup, CreateStatusRecorder());

        var result = await sut.HandleHorseHistoryRequestAsync(
            new HorseHistoryCollectionRequestPayload("horse-1", "race-1", "JRA"));

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsTrue(result.Message?.Contains("RaceHistoryEntriesPersisted=0/1", StringComparison.Ordinal));
        Assert.IsEmpty(writeService.UpsertedRaces);
    }

    [TestMethod]
    public async Task HandleJockeyHistoryRequestAsync_SynchronizesJockeyProfileSuccessfully()
    {
        var raceQueryService = new StubRaceQueryService
        {
            Jockey = new JockeyReadModel
            {
                JockeyId = "jockey-1",
                DisplayName = "横山武史",
                NormalizedName = "横山武史",
            },
        };
        var writeService = new StubDataCollectionWriteService();
        var jockeyProfile = new JraEntityProfile(
            "jockey",
            "横山武史",
            null,
            null,
            "美浦",
            2020,
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>(),
            "https://example.test/jockey");
        var profileLookup = new StubJraProfileLookup
        {
            JockeyProfile = new JraExtractionEnvelope<JraEntityProfile>(
                true,
                JraPageKind.JockeyProfile,
                "https://example.test/jockey",
                new JraNavigationTrace(Array.Empty<string>(), TimeSpan.Zero),
                jockeyProfile),
        };
        var sut = new JraHistoricalDataRequestHandler(raceQueryService, writeService, profileLookup, CreateStatusRecorder());

        var result = await sut.HandleJockeyHistoryRequestAsync(
            new JockeyHistoryCollectionRequestPayload("jockey-1", "race-1", "JRA"));

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual("横山武史", writeService.UpsertedJockeyName);
        Assert.AreEqual("美浦", writeService.UpsertedJockeyAffiliationCode);
        Assert.IsTrue(result.Message?.Contains("profile synchronized", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task HandleTrainerProfileRequestAsync_SynchronizesTrainerProfileSuccessfully()
    {
        var raceQueryService = new StubRaceQueryService
        {
            Trainer = new TrainerReadModel
            {
                TrainerId = "trainer-1",
                DisplayName = "手塚貴久",
                NormalizedName = "手塚貴久",
            },
        };
        var profile = new JraEntityProfile(
            "trainer", "手塚貴久", null, null, "美浦", 1999,
            null, null, null, null, null,
            new Dictionary<string, string>(), "https://example.test/trainer");
        var profileLookup = new StubJraProfileLookup
        {
            TrainerProfile = new JraExtractionEnvelope<JraEntityProfile>(
                true, JraPageKind.TrainerProfile, profile.SourceUrl,
                new JraNavigationTrace(Array.Empty<string>(), TimeSpan.Zero), profile),
        };
        var writeService = new StubDataCollectionWriteService();
        var sut = new JraHistoricalDataRequestHandler(raceQueryService, writeService, profileLookup, CreateStatusRecorder());

        var result = await sut.HandleTrainerProfileRequestAsync(
            new TrainerProfileCollectionRequestPayload("trainer-1", "race-1", "JRA"));

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual("手塚貴久", writeService.UpsertedTrainerName);
        Assert.AreEqual("美浦", writeService.UpsertedTrainerAffiliationCode);
    }

    private AgentAcquisitionStatusRecorder CreateStatusRecorder()
    {
        var options = Options.Create(new AgentProcessingOptions
        {
            StateDirectory = _stateDirectory,
            PredictionLeaseMinutes = 5,
            CollectionLeaseMinutes = 5,
        });

        return new AgentAcquisitionStatusRecorder(
            new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance));
    }

    private sealed class StubJraProfileLookup : IJraProfileLookup
    {
        public JraExtractionEnvelope<JraEntityProfile>? HorseProfile { get; set; }

        public JraExtractionEnvelope<JraHorseProfileData>? HorseProfileWithHistory { get; set; }

        public JraExtractionEnvelope<JraEntityProfile>? JockeyProfile { get; set; }
        public JraExtractionEnvelope<JraEntityProfile>? TrainerProfile { get; set; }

        public Task<JraExtractionEnvelope<JraEntityProfile>> GetHorseProfileAsync(string horseName, CancellationToken cancellationToken = default)
            => Task.FromResult(HorseProfile!);

        public Task<JraExtractionEnvelope<JraHorseProfileData>> GetHorseProfileWithHistoryAsync(string horseName, CancellationToken cancellationToken = default)
            => Task.FromResult(HorseProfileWithHistory!);

        public Task<JraExtractionEnvelope<JraEntityProfile>> GetJockeyProfileAsync(string jockeyName, CancellationToken cancellationToken = default)
            => Task.FromResult(JockeyProfile!);

        public Task<JraExtractionEnvelope<JraEntityProfile>> GetTrainerProfileAsync(string trainerName, CancellationToken cancellationToken = default)
            => Task.FromResult(TrainerProfile!);
    }

    private sealed class StubDataCollectionWriteService : IDataCollectionWriteService
    {
        public string? UpsertedHorseBirthDate { get; private set; }

        public string? UpsertedHorseName { get; private set; }

        public string? UpsertedHorseSexCode { get; private set; }
        public string? UpsertedHorseOwnerName { get; private set; }

        public string? UpsertedJockeyAffiliationCode { get; private set; }

        public string? UpsertedJockeyName { get; private set; }
        public string? UpsertedTrainerName { get; private set; }
        public string? UpsertedTrainerAffiliationCode { get; private set; }

        public List<(string RaceDate, string RacecourseCode, int RaceNumber, string RaceName)> UpsertedRaces { get; } = [];

        public List<(string RaceId, int HorseNumber, string HorseName)> UpsertedRaceEntries { get; } = [];

        public List<(string RaceId, string WinningHorseName)> DeclaredRaceResults { get; } = [];

        public List<(string RaceId, int HorseNumber, int? FinishPosition)> DeclaredRaceEntryResults { get; } = [];

        public Task<string> UpsertHorseAsync(string registeredName, string? normalizedName, string? sexCode, string? birthDate, CancellationToken cancellationToken = default)
        {
            UpsertedHorseName = registeredName;
            UpsertedHorseSexCode = sexCode;
            UpsertedHorseBirthDate = birthDate;
            return Task.FromResult("horse-1");
        }

        public Task<string> UpsertHorseWithOwnerAsync(string registeredName, string? normalizedName, string? sexCode, string? birthDate, string? ownerName, CancellationToken cancellationToken = default)
        {
            UpsertedHorseOwnerName = ownerName;
            return UpsertHorseAsync(registeredName, normalizedName, sexCode, birthDate, cancellationToken);
        }

        public Task<string> UpsertJockeyAsync(string displayName, string? normalizedName, string? affiliationCode, CancellationToken cancellationToken = default)
        {
            UpsertedJockeyName = displayName;
            UpsertedJockeyAffiliationCode = affiliationCode;
            return Task.FromResult("jockey-1");
        }

        public Task<string> UpsertRaceAsync(string raceDate, string racecourseCode, int raceNumber, string raceName, int? entryCount, string? gradeCode, string? surfaceCode, int? distanceMeters, string? directionCode, CancellationToken cancellationToken = default)
        {
            UpsertedRaces.Add((raceDate, racecourseCode, raceNumber, raceName));
            return Task.FromResult($"race-{UpsertedRaces.Count}");
        }

        public Task<string> UpsertTrainerAsync(string displayName, string? normalizedName, string? affiliationCode, CancellationToken cancellationToken = default)
        {
            UpsertedTrainerName = displayName;
            UpsertedTrainerAffiliationCode = affiliationCode;
            return Task.FromResult("trainer-1");
        }

        public Task<string> UpsertRaceEntryAsync(string raceId, int horseNumber, string horseName, string? jockeyName, string? trainerName, int? gateNumber, decimal? assignedWeight, string? sexCode, int? age, decimal? declaredWeight, decimal? declaredWeightDiff, CancellationToken cancellationToken = default)
        {
            UpsertedRaceEntries.Add((raceId, horseNumber, horseName));
            return Task.FromResult($"{raceId}-{horseNumber}");
        }

        public Task<string> DeclareRaceResultAsync(string raceId, string winningHorseName, string? declaredAt, string? winningHorseId, CancellationToken cancellationToken = default)
        {
            DeclaredRaceResults.Add((raceId, winningHorseName));
            return Task.FromResult(raceId);
        }

        public Task<string> DeclareRaceEntryResultAsync(string raceId, int horseNumber, int? finishPosition, string? officialTime, string? marginText, string? lastThreeFurlongTime, string? abnormalResultCode, decimal? prizeMoney, CancellationToken cancellationToken = default)
        {
            DeclaredRaceEntryResults.Add((raceId, horseNumber, finishPosition));
            return Task.FromResult($"{raceId}-{horseNumber}");
        }

        public Task<string> DeclareRacePayoutsAsync(string raceId, string? winPayoutsJson, string? placePayoutsJson, string? quinellaPayoutsJson, string? exactaPayoutsJson, string? trifectaPayoutsJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubRaceQueryService : IRaceQueryService
    {
        public HorseReadModel? Horse { get; set; }

        public JockeyReadModel? Jockey { get; set; }
        public TrainerReadModel? Trainer { get; set; }

        public Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult(Horse);

        public Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult(Jockey);

        public Task<TrainerReadModel?> GetTrainerAsync(string trainerId, CancellationToken cancellationToken = default)
            => Task.FromResult(Trainer);

        public Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MlPredictionResponse?> GetMlPredictionAsync(string raceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PredictionTicketSummaryReadModel?> GetPredictionTicketAsync(string predictionTicketId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
#endif
