using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Agents.Contracts;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

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
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task HandleHorseHistoryRequestAsync_SynchronizesHorseProfileBeforePermanentFailure()
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
        var profileLookup = new StubJraProfileLookup
        {
            HorseProfile = new JraExtractionEnvelope<JraEntityProfile>(
                true,
                JraPageKind.HorseProfile,
                "https://example.test/horse",
                new JraNavigationTrace(Array.Empty<string>(), TimeSpan.Zero),
                horseProfile),
        };
        var sut = new JraHistoricalDataRequestHandler(raceQueryService, writeService, profileLookup, CreateStatusRecorder());

        var result = await sut.HandleHorseHistoryRequestAsync(
            new HorseHistoryCollectionRequestPayload("horse-1", "race-1", "JRA"));

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.IsPermanentFailure);
        Assert.AreEqual("ソールオリエンス", writeService.UpsertedHorseName);
        Assert.AreEqual("牡", writeService.UpsertedHorseSexCode);
        Assert.AreEqual("2020-02-24", writeService.UpsertedHorseBirthDate);
        Assert.IsTrue(result.Message?.Contains("profile synchronized", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task HandleJockeyHistoryRequestAsync_SynchronizesJockeyProfileBeforePermanentFailure()
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

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.IsPermanentFailure);
        Assert.AreEqual("横山武史", writeService.UpsertedJockeyName);
        Assert.AreEqual("美浦", writeService.UpsertedJockeyAffiliationCode);
        Assert.IsTrue(result.Message?.Contains("profile synchronized", StringComparison.OrdinalIgnoreCase));
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

        public JraExtractionEnvelope<JraEntityProfile>? JockeyProfile { get; set; }

        public Task<JraExtractionEnvelope<JraEntityProfile>> GetHorseProfileAsync(string horseName, CancellationToken cancellationToken = default)
            => Task.FromResult(HorseProfile!);

        public Task<JraExtractionEnvelope<JraEntityProfile>> GetJockeyProfileAsync(string jockeyName, CancellationToken cancellationToken = default)
            => Task.FromResult(JockeyProfile!);
    }

    private sealed class StubDataCollectionWriteService : IDataCollectionWriteService
    {
        public string? UpsertedHorseBirthDate { get; private set; }

        public string? UpsertedHorseName { get; private set; }

        public string? UpsertedHorseSexCode { get; private set; }

        public string? UpsertedJockeyAffiliationCode { get; private set; }

        public string? UpsertedJockeyName { get; private set; }

        public Task<string> UpsertHorseAsync(string registeredName, string? normalizedName, string? sexCode, string? birthDate, CancellationToken cancellationToken = default)
        {
            UpsertedHorseName = registeredName;
            UpsertedHorseSexCode = sexCode;
            UpsertedHorseBirthDate = birthDate;
            return Task.FromResult("horse-1");
        }

        public Task<string> UpsertJockeyAsync(string displayName, string? normalizedName, string? affiliationCode, CancellationToken cancellationToken = default)
        {
            UpsertedJockeyName = displayName;
            UpsertedJockeyAffiliationCode = affiliationCode;
            return Task.FromResult("jockey-1");
        }

        public Task<string> UpsertRaceAsync(string raceDate, string racecourseCode, int raceNumber, string raceName, int? entryCount, string? gradeCode, string? surfaceCode, int? distanceMeters, string? directionCode, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> UpsertTrainerAsync(string displayName, string? normalizedName, string? affiliationCode, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> UpsertRaceEntryAsync(string raceId, int horseNumber, string horseName, string? jockeyName, string? trainerName, int? gateNumber, decimal? assignedWeight, string? sexCode, int? age, decimal? declaredWeight, decimal? declaredWeightDiff, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> DeclareRaceResultAsync(string raceId, string winningHorseName, string? declaredAt, string? winningHorseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> DeclareRaceEntryResultAsync(string raceId, int horseNumber, int? finishPosition, string? officialTime, string? marginText, string? lastThreeFurlongTime, string? abnormalResultCode, decimal? prizeMoney, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> DeclareRacePayoutsAsync(string raceId, string? winPayoutsJson, string? placePayoutsJson, string? quinellaPayoutsJson, string? exactaPayoutsJson, string? trifectaPayoutsJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubRaceQueryService : IRaceQueryService
    {
        public HorseReadModel? Horse { get; set; }

        public JockeyReadModel? Jockey { get; set; }

        public Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult(Horse);

        public Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult(Jockey);

        public Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}