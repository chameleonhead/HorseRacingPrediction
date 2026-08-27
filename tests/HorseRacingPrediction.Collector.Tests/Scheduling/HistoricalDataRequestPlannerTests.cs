using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class HistoricalDataRequestPlannerTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "historical-request-planner-tests", Guid.NewGuid().ToString("N"));
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
    public async Task EnsureRequestsForRaceAsync_WhenHistoryIsMissing_SchedulesEntityRequests()
    {
        var historicalRaceReferenceCollector = new StubHistoricalRaceReferenceCollector
        {
            References =
            [
                new HistoricalRaceReference(new DateOnly(2026, 4, 13), "中山", 11),
                new HistoricalRaceReference(new DateOnly(2026, 3, 2), "中山", 9),
            ]
        };
        var raceQueryService = new StubRaceQueryService
        {
            RaceContext = new RacePredictionContextReadModel
            {
                RaceId = "race-1",
                RaceDate = new DateOnly(2026, 5, 16),
                RacecourseCode = "06",
                RaceNumber = 11,
                Entries =
                [
                    new RacePredictionContextEntry("entry-1", "horse-1", 1, "jockey-1", "trainer-1", null, null, null, null, null, null, null)
                ]
            }
        };

        var store = CreateStore();
        var planner = new HistoricalDataRequestPlanner(
            raceQueryService,
            store,
            historicalRaceReferenceCollector,
            NullLogger<HistoricalDataRequestPlanner>.Instance);

        var plan = await planner.EnsureRequestsForRaceAsync(
            "race-1",
            new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(1, plan.RequestedHorseHistoryCount);
        Assert.AreEqual(1, plan.RequestedJockeyHistoryCount);
        Assert.AreEqual(2, plan.RequestedRaceResultCount);
        Assert.AreEqual(1, plan.RequestedTrainerProfileCount);

        var activeHorsePayloads = await store.GetActiveJobPayloadsAsync(AgentJobType.HorseHistoryCollectionRequest);
        var activeJockeyPayloads = await store.GetActiveJobPayloadsAsync(AgentJobType.JockeyHistoryCollectionRequest);
        var activeRaceResultPayloads = await store.GetActiveJobPayloadsAsync(AgentJobType.HistoricalRaceResultCollectionRequest);
        var activeTrainerPayloads = await store.GetActiveJobPayloadsAsync(AgentJobType.TrainerProfileCollectionRequest);

        Assert.HasCount(1, activeHorsePayloads);
        Assert.HasCount(1, activeJockeyPayloads);
        Assert.HasCount(2, activeRaceResultPayloads);
        Assert.HasCount(1, activeTrainerPayloads);

        var horsePayload = AgentJobPayloadSerializer.Deserialize<HorseHistoryCollectionRequestPayload>(activeHorsePayloads[0]);
        Assert.AreEqual("horse-1", horsePayload.HorseId);
        Assert.AreEqual("race-1", horsePayload.RequestedByRaceId);

        var trainerPayload = AgentJobPayloadSerializer.Deserialize<TrainerProfileCollectionRequestPayload>(activeTrainerPayloads[0]);
        Assert.AreEqual("trainer-1", trainerPayload.TrainerId);
        Assert.AreEqual("race-1", trainerPayload.RequestedByRaceId);
    }

    [TestMethod]
    public async Task EnsureRequestsForRaceAsync_WhenSameHorseAppearsMoreThanOnce_SchedulesOneHorseRequest()
    {
        var historicalRaceReferenceCollector = new StubHistoricalRaceReferenceCollector
        {
            References =
            [
                new HistoricalRaceReference(new DateOnly(2026, 4, 13), "中山", 11),
            ]
        };
        var raceQueryService = new StubRaceQueryService
        {
            RaceContext = new RacePredictionContextReadModel
            {
                RaceId = "race-1",
                RaceDate = new DateOnly(2026, 5, 16),
                RacecourseCode = "06",
                RaceNumber = 11,
                Entries =
                [
                    new RacePredictionContextEntry("entry-1", "horse-1", 1, null, null, null, null, null, null, null, null, null),
                    new RacePredictionContextEntry("entry-2", "horse-1", 2, null, null, null, null, null, null, null, null, null)
                ]
            }
        };

        var store = CreateStore();
        var planner = new HistoricalDataRequestPlanner(
            raceQueryService,
            store,
            historicalRaceReferenceCollector,
            NullLogger<HistoricalDataRequestPlanner>.Instance);

        var plan = await planner.EnsureRequestsForRaceAsync(
            "race-1",
            new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(1, plan.RequestedHorseHistoryCount);

        var activeHorsePayloads = await store.GetActiveJobPayloadsAsync(AgentJobType.HorseHistoryCollectionRequest);
        Assert.HasCount(1, activeHorsePayloads);
    }

    [TestMethod]
    public async Task EnsureRequestsForRaceAsync_WhenHistoryExistsButProfilesAreIncomplete_SchedulesProfileRequests()
    {
        var raceQueryService = new StubRaceQueryService
        {
            RaceContext = CreateSingleEntryContext(),
            Horse = new HorseReadModel
            {
                HorseId = "horse-1",
                RegisteredName = "テストホース",
                NormalizedName = "テストホース",
                SexCode = "M"
            },
            HorseHistory = new HorseRaceHistoryReadModel
            {
                HorseId = "horse-1",
                Entries = [CreateHorseHistoryEntry()]
            },
            Jockey = new JockeyReadModel
            {
                JockeyId = "jockey-1",
                DisplayName = "テスト騎手",
                NormalizedName = "テスト騎手"
            },
            JockeyHistory = new JockeyRaceHistoryReadModel
            {
                JockeyId = "jockey-1",
                Entries = [CreateJockeyHistoryEntry()]
            },
            Trainer = new TrainerReadModel
            {
                TrainerId = "trainer-1",
                DisplayName = "テスト調教師",
                NormalizedName = "テスト調教師"
            }
        };

        var store = CreateStore();
        var planner = new HistoricalDataRequestPlanner(
            raceQueryService,
            store,
            new StubHistoricalRaceReferenceCollector(),
            NullLogger<HistoricalDataRequestPlanner>.Instance);

        var plan = await planner.EnsureRequestsForRaceAsync(
            "race-1",
            new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(1, plan.RequestedHorseHistoryCount);
        Assert.AreEqual(1, plan.RequestedJockeyHistoryCount);
        Assert.AreEqual(1, plan.RequestedTrainerProfileCount);
    }

    [TestMethod]
    public async Task EnsureRequestsForRaceAsync_WhenProfilesAndHistoryAreComplete_DoesNotScheduleEntityRequests()
    {
        var raceQueryService = new StubRaceQueryService
        {
            RaceContext = CreateSingleEntryContext(),
            Horse = new HorseReadModel
            {
                HorseId = "horse-1",
                RegisteredName = "テストホース",
                NormalizedName = "テストホース",
                SexCode = "M",
                BirthDate = new DateOnly(2020, 3, 1),
                OwnerName = "テスト馬主"
            },
            HorseHistory = new HorseRaceHistoryReadModel
            {
                HorseId = "horse-1",
                Entries = [CreateHorseHistoryEntry()]
            },
            Jockey = new JockeyReadModel
            {
                JockeyId = "jockey-1",
                DisplayName = "テスト騎手",
                NormalizedName = "テスト騎手",
                AffiliationCode = "美浦"
            },
            JockeyHistory = new JockeyRaceHistoryReadModel
            {
                JockeyId = "jockey-1",
                Entries = [CreateJockeyHistoryEntry()]
            },
            Trainer = new TrainerReadModel
            {
                TrainerId = "trainer-1",
                DisplayName = "テスト調教師",
                NormalizedName = "テスト調教師",
                AffiliationCode = "栗東"
            }
        };

        var planner = new HistoricalDataRequestPlanner(
            raceQueryService,
            CreateStore(),
            new StubHistoricalRaceReferenceCollector(),
            NullLogger<HistoricalDataRequestPlanner>.Instance);

        var plan = await planner.EnsureRequestsForRaceAsync(
            "race-1",
            new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(0, plan.RequestedHorseHistoryCount);
        Assert.AreEqual(0, plan.RequestedJockeyHistoryCount);
        Assert.AreEqual(0, plan.RequestedTrainerProfileCount);
    }

    private static RacePredictionContextReadModel CreateSingleEntryContext() => new()
    {
        RaceId = "race-1",
        RaceDate = new DateOnly(2026, 5, 16),
        RacecourseCode = "06",
        RaceNumber = 11,
        Entries =
        [
            new RacePredictionContextEntry("entry-1", "horse-1", 1, "jockey-1", "trainer-1", null, null, null, null, null, null, null)
        ]
    };

    private static HorseRaceHistoryEntry CreateHorseHistoryEntry()
        => new("past-race", "past-entry", new DateOnly(2026, 4, 1), "中山", "芝", 1600, null, null, null, null, null, null, null, "jockey-1", "trainer-1", 1, null, null, null);

    private static JockeyRaceHistoryEntry CreateJockeyHistoryEntry()
        => new("past-race", "past-entry", "horse-1", new DateOnly(2026, 4, 1), "中山", "芝", 1600, null, null, 1, null);

    private ProcessingStateStore CreateStore()
    {
        var options = Options.Create(new AgentProcessingOptions
        {
            StateDirectory = _stateDirectory,
            PredictionLeaseMinutes = 5,
            CollectionLeaseMinutes = 5
        });

        return new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance);
    }

    private sealed class StubRaceQueryService : IRaceQueryService
    {
        public RacePredictionContextReadModel? RaceContext { get; set; }
        public HorseReadModel? Horse { get; set; }
        public JockeyReadModel? Jockey { get; set; }
        public TrainerReadModel? Trainer { get; set; }
        public HorseRaceHistoryReadModel? HorseHistory { get; set; }
        public JockeyRaceHistoryReadModel? JockeyHistory { get; set; }

        public Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RaceSearchSummary>>([]);

        public Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
            => Task.FromResult(RaceContext);

        public Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult(Horse);

        public Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult(Jockey);

        public Task<TrainerReadModel?> GetTrainerAsync(string trainerId, CancellationToken cancellationToken = default)
            => Task.FromResult(Trainer);

        public Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
            => Task.FromResult<MemoBySubjectReadModel?>(null);

        public Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult(HorseHistory);

        public Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult(JockeyHistory);

        public Task<MlPredictionResponse?> GetMlPredictionAsync(string raceId, CancellationToken cancellationToken = default)
            => Task.FromResult<MlPredictionResponse?>(null);

        public Task<PredictionTicketSummaryReadModel?> GetPredictionTicketAsync(string predictionTicketId, CancellationToken cancellationToken = default)
            => Task.FromResult<PredictionTicketSummaryReadModel?>(null);
    }

    private sealed class StubHistoricalRaceReferenceCollector : IHistoricalRaceReferenceCollector
    {
        public IReadOnlyList<HistoricalRaceReference> References { get; set; } = Array.Empty<HistoricalRaceReference>();

        public Task<IReadOnlyList<HistoricalRaceReference>> CollectAsync(DateOnly raceDate, string racecourse, int raceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(References);
    }
}
