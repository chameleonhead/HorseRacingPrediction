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
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task EnsureRequestsForRaceAsync_WhenHistoryIsMissing_SchedulesHorseAndJockeyRequests()
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
                    new RacePredictionContextEntry("entry-1", "horse-1", 1, "jockey-1", null, null, null, null, null, null, null, null)
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

        var activeHorsePayloads = await store.GetActiveJobPayloadsAsync(AgentJobType.HorseHistoryCollectionRequest);
        var activeJockeyPayloads = await store.GetActiveJobPayloadsAsync(AgentJobType.JockeyHistoryCollectionRequest);
        var activeRaceResultPayloads = await store.GetActiveJobPayloadsAsync(AgentJobType.HistoricalRaceResultCollectionRequest);

        Assert.HasCount(1, activeHorsePayloads);
        Assert.HasCount(1, activeJockeyPayloads);
        Assert.HasCount(2, activeRaceResultPayloads);

        var horsePayload = AgentJobPayloadSerializer.Deserialize<HorseHistoryCollectionRequestPayload>(activeHorsePayloads[0]);
        Assert.AreEqual("horse-1", horsePayload.HorseId);
        Assert.AreEqual("race-1", horsePayload.RequestedByRaceId);
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

        public Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RaceSearchSummary>>([]);

        public Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
            => Task.FromResult(RaceContext);

        public Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult<HorseReadModel?>(null);

        public Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult<JockeyReadModel?>(null);

        public Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
            => Task.FromResult<MemoBySubjectReadModel?>(null);

        public Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult<HorseRaceHistoryReadModel?>(null);

        public Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult<JockeyRaceHistoryReadModel?>(null);

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
