using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Contracts;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class RaceEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static WebApplication _app = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_app, _client) = await TestApplicationFactory.CreateAsync();
        _client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
    }

    [ClassCleanup]
    public static async Task ClassClean()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateRace_ReturnsCreated()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var request = new CreateRaceRequest(
            new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId);

        var response = await _client.PostAsJsonAsync("/api/races", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.ToString().Contains($"/api/races/{raceId}"));
    }

    [TestMethod]
    public async Task CreateRace_WhenAlreadyExists_ReturnsConflict()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var request = new CreateRaceRequest(
            new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId);

        var firstResponse = await _client.PostAsJsonAsync("/api/races", request, JsonOptions);
        var secondResponse = await _client.PostAsJsonAsync("/api/races", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [TestMethod]
    public async Task GetRace_AfterCreate_ReturnsCorrectData()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var request = new CreateRaceRequest(
            new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId);
        await _client.PostAsJsonAsync("/api/races", request, JsonOptions);

        var response = await _client.GetAsync($"/api/races/{raceId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var race = await response.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(race);
        Assert.AreEqual(raceId, race.RaceId);
        Assert.AreEqual("TOKYO", race.RacecourseCode);
        Assert.AreEqual(5, race.RaceNumber);
        Assert.AreEqual("皐月賞", race.RaceName);
        Assert.AreEqual(RaceStatus.Draft, race.Status);
    }

    [TestMethod]
    public async Task CreateRace_WithCollectedMetadata_PersistsMetadata()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var request = new CreateRaceRequest(
            new DateOnly(2026, 8, 9), "札幌", 4, "3歳未勝利", raceId,
            GradeCode: "未勝利",
            SurfaceCode: "ダート",
            DistanceMeters: 1700,
            DirectionCode: "右");

        var createResponse = await _client.PostAsJsonAsync("/api/races", request, JsonOptions);
        var race = await _client.GetFromJsonAsync<RaceResponse>($"/api/races/{raceId}", JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.IsNotNull(race);
        Assert.AreEqual("未勝利", race.GradeCode);
        Assert.AreEqual("ダート", race.SurfaceCode);
        Assert.AreEqual(1700, race.DistanceMeters);
        Assert.AreEqual("右", race.DirectionCode);
    }

    [TestMethod]
    public async Task DeclareRaceResultBulk_InfersGradeFromRaceNameWhenParserGradeIsMissing()
    {
        var date = new DateOnly(2026, 9, 6);
        var course = $"TEST-{Guid.NewGuid():N}";
        const int raceNumber = 11;
        var raceId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildRaceId(date, course, raceNumber);
        var raceName = "第40回 産経賞セントウルステークス GⅡ";
        await _client.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(date, course, raceNumber, raceName, raceId), JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/races/result-bulk",
            new DeclareRaceResultBulkRequest(date, course, raceNumber, raceName), JsonOptions);
        var race = await _client.GetFromJsonAsync<RaceResponse>($"/api/races/{raceId}", JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(race);
        Assert.AreEqual("G2", race.GradeCode);
    }

    [TestMethod]
    public async Task DeclareRaceResultBulk_WithEighteenEntries_PersistsOnceAndReplayAddsNoEvents()
    {
        var date = new DateOnly(2026, 9, 13);
        var course = $"BULK-{Guid.NewGuid():N}";
        const int raceNumber = 12;
        var observedAt = new DateTimeOffset(2026, 9, 13, 16, 0, 0, TimeSpan.FromHours(9));
        var entries = Enumerable.Range(1, 18).Select(number => new RaceResultEntryBulkDto(
            number, number, $"1:{30 + number:00}.0", null, $"3{number % 10}.0", null,
            1_000_000m - number, $"一括馬{number}", $"一括騎手{number}", $"一括調教師{number}",
            number, 55m, number % 2 == 0 ? "F" : "M", 3, number,
            450 + number, number % 3, number, false, $"馬主{number}", $"{number}", 12.3m,
            AdditionalPrizeMoney: 10_000m)).ToArray();
        var request = new DeclareRaceResultBulkRequest(date, course, raceNumber, "一括登録検証",
            EntryCount: 18, WinningHorseName: "一括馬1", DeclaredAt: observedAt, Entries: entries,
            Weather: new(observedAt, "SUNNY", "晴", 24m, 50m, "N", 2m),
            TrackCondition: new(observedAt, "GOOD", "GOOD", "良"),
            Payouts: new(observedAt, [new("1", 250m)], null, null, null, null));

        var first = await _client.PostAsJsonAsync("/api/races/result-bulk", request, JsonOptions);
        var firstBody = await first.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>(JsonOptions);
        var raceId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildRaceId(date, course, raceNumber);
        var race = await _client.GetFromJsonAsync<RaceResponse>($"/api/races/{raceId}", JsonOptions);
        var eventsAfterFirst = CountStoredEvents();
        var replay = await _client.PostAsJsonAsync("/api/races/result-bulk", request, JsonOptions);
        var eventsAfterReplay = CountStoredEvents();

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.IsNotNull(firstBody);
        Assert.IsEmpty(firstBody.Errors);
        Assert.HasCount(18, firstBody.Outcomes!);
        Assert.IsTrue(firstBody.Outcomes!.All(item => item.Status == "Accepted"));
        Assert.IsNotNull(race);
        Assert.HasCount(18, race.Entries);
        Assert.HasCount(18, race.EntryResults);
        Assert.AreEqual(1, race.EntryResults[0].Popularity);
        Assert.AreEqual(12.3m, race.EntryResults[0].Average1F);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(eventsAfterFirst, eventsAfterReplay);
        foreach (var entry in race.Entries)
        {
            var horse = await _client.GetAsync($"/api/horses/{entry.HorseId}");
            Assert.AreEqual(HttpStatusCode.OK, horse.StatusCode,
                "Result-only replay must leave every referenced Horse materialized.");
        }
    }

    [TestMethod]
    public async Task DeclareRaceResultBulk_RaceCardUsesHorseSourceIdentityForCanonicalEntryId()
    {
        var date = new DateOnly(2026, 9, 19);
        var course = $"IDENTITY-{Guid.NewGuid():N}";
        const string horseName = "識別子付き競走馬";
        const string sourceIdentity =
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026123456/00";
        var request = new DeclareRaceResultBulkRequest(date, course, 1, "主体ID検証",
            EntryCount: 1,
            Entries:
            [
                new RaceResultEntryBulkDto(1, 1, "1:35.0", null, null, null, null,
                    HorseName: horseName, JockeyName: "▲識別 騎手", TrainerName: "識別 調教師（美浦）",
                    HorseSourceIdentity: sourceIdentity),
            ],
            WinningHorseName: horseName, DeclaredAt: DateTimeOffset.UtcNow);

        var response = await _client.PostAsJsonAsync("/api/races/result-bulk", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>(JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.IsEmpty(body.Errors);
        var race = await _client.GetFromJsonAsync<RaceResponse>($"/api/races/{body.RaceId}", JsonOptions);
        Assert.IsNotNull(race);
        var entry = race.Entries.Single();
        Assert.AreEqual(HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildHorseId(
            horseName, sourceIdentity), entry.HorseId);
        Assert.AreEqual(HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId(
            "jockey", HorseRacingPrediction.ApiClient.DeterministicIdGenerator.NormalizeKey("識別 騎手")), entry.JockeyId);
        Assert.AreEqual(HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId(
            "trainer", HorseRacingPrediction.ApiClient.DeterministicIdGenerator.NormalizeKey("識別 調教師")), entry.TrainerId);
    }

    [TestMethod]
    public async Task RefreshRaceCard_SourceIdentityReplacesLegacyNameBasedHorseId()
    {
        var date = new DateOnly(2030, 1, 2);
        const string course = "中山";
        const string horseName = "既存ID補正馬";
        const string sourceIdentity =
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026654321/00";
        var initial = new DeclareRaceResultBulkRequest(date, course, 2, "既存ID補正",
            EntryCount: 1, WinningHorseName: horseName, DeclaredAt: DateTimeOffset.UtcNow,
            Entries: [new(1, 1, "1:36.0", null, null, null, null, HorseName: horseName)]);
        var initialResponse = await _client.PostAsJsonAsync("/api/races/result-bulk", initial, JsonOptions);
        var initialBody = await initialResponse.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>(JsonOptions);
        Assert.IsNotNull(initialBody);

        var refresh = new DeclareRaceResultBulkRequest(date, course, 2, "既存ID補正", EntryCount: 1,
            Entries:
            [
                new(1, null, null, null, null, null, null, HorseName: horseName,
                    HorseSourceIdentity: sourceIdentity),
            ],
            TargetRaceId: initialBody.RaceId, RefreshExistingData: true, IsRaceCard: true);
        var refreshResponse = await _client.PostAsJsonAsync("/api/races/result-bulk", refresh, JsonOptions);
        var race = await _client.GetFromJsonAsync<RaceResponse>($"/api/races/{initialBody.RaceId}", JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.IsNotNull(race);
        Assert.AreEqual(HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildHorseId(
            horseName, sourceIdentity), race.Entries.Single().HorseId);
    }

    [TestMethod]
    public async Task DeclareRaceResultBulk_InvalidEntries_ReturnsStructuredRejections()
    {
        var response = await _client.PostAsJsonAsync("/api/races/result-bulk",
            new DeclareRaceResultBulkRequest(new DateOnly(2026, 9, 14), $"INVALID-{Guid.NewGuid():N}", 1,
                "入力検証", Entries: [new(0, null, null, null, null, null, null)]), JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>(JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.HasCount(1, body.Outcomes!);
        Assert.AreEqual("Rejected", body.Outcomes![0].Status);
        Assert.AreEqual("InvalidHorseNumber", body.Outcomes[0].ErrorCode);
    }

    [TestMethod]
    public async Task DeclareRaceResultBulk_InvalidRaceState_MutatesNeitherRaceNorRelatedSubjects()
    {
        var eventsBefore = CountStoredEvents();
        var response = await _client.PostAsJsonAsync("/api/races/result-bulk",
            new DeclareRaceResultBulkRequest(new DateOnly(2026, 9, 14), $"PREVALIDATE-{Guid.NewGuid():N}", 2,
                "事前検証", EntryCount: 1,
                Entries: [new(1, 1, "1:40.0", null, null, null, null, HorseName: "残してはいけない馬")]),
            JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>(JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.IsNotEmpty(body.Errors);
        Assert.AreEqual("Failed", body.Outcomes![0].Status);
        Assert.AreEqual("RaceBulkValidationFailed", body.Outcomes[0].ErrorCode);
        Assert.AreEqual(eventsBefore, CountStoredEvents());
    }

    private static int CountStoredEvents()
    {
        var provider = _app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>();
        using var db = provider.CreateContext();
        return db.Set<EventEntity>().Count();
    }

    [TestMethod]
    public async Task GetRace_UsesHorseProfileOwner_WhenEntryOwnerIsMissing()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2026, 8, 9), "札幌", 4, "3歳未勝利", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("テストホース", "テストホース", "M", null, horseId, "テスト馬主"),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(1),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horseId, 1, null, null, null, null, null, null, null, null),
            JsonOptions);

        var race = await _client.GetFromJsonAsync<RaceResponse>($"/api/races/{raceId}", JsonOptions);

        Assert.IsNotNull(race);
        Assert.AreEqual(1, race.Entries.Count);
        Assert.AreEqual("テスト馬主", race.Entries[0].OwnerName);
    }

    [TestMethod]
    public async Task SearchRaces_FiltersSortsAndPages()
    {
        var key = Guid.NewGuid().ToString("N");
        var tokyoRace1 = $"race-{Guid.NewGuid()}";
        var tokyoRace2 = $"race-{Guid.NewGuid()}";
        var nakayamaRace = $"race-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 3, $"RaceSearch-{key}-A", tokyoRace1),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 7, $"RaceSearch-{key}-B", tokyoRace2),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "NAKAYAMA", 11, $"RaceSearch-{key}-C", nakayamaRace),
            JsonOptions);

        var response = await _client.GetAsync($"/api/races?racecourseCode=TOKYO&raceName=RaceSearch-{key}&page=2&pageSize=1&sortBy=raceNumber&sortDescending=false");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<RaceSummaryResponse>>(JsonOptions);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual(2, result.TotalPages);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(tokyoRace2, result.Items[0].RaceId);
        Assert.AreEqual(7, result.Items[0].RaceNumber);
    }

    [TestMethod]
    public async Task SearchRaces_AfterAppRestart_UsesPersistedRaceSummaryReadModel()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"race-summary-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        var raceId = $"race-{Guid.NewGuid()}";

        try
        {
            var (firstApp, firstClient) = await TestApplicationFactory.CreateAsync(connectionString);
            firstClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            var createResponse = await firstClient.PostAsJsonAsync(
                "/api/races",
                new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 9, "Restart Persistence Cup", raceId),
                JsonOptions);

            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

            firstClient.Dispose();
            await firstApp.DisposeAsync();

            var (secondApp, secondClient) = await TestApplicationFactory.CreateAsync(connectionString);
            secondClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            try
            {
                var getResponse = await secondClient.GetAsync($"/api/races/{raceId}");
                Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

                var race = await getResponse.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
                Assert.IsNotNull(race);
                Assert.AreEqual(raceId, race.RaceId);

                var searchResponse = await secondClient.GetAsync($"/api/races?raceId={raceId}");
                Assert.AreEqual(HttpStatusCode.OK, searchResponse.StatusCode);

                var result = await searchResponse.Content.ReadFromJsonAsync<PagedResponse<RaceSummaryResponse>>(JsonOptions);
                Assert.IsNotNull(result);
                Assert.AreEqual(1, result.TotalCount);
                Assert.AreEqual(raceId, result.Items[0].RaceId);
            }
            finally
            {
                secondClient.Dispose();
                await secondApp.DisposeAsync();
            }
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task PublishCard_AfterCreate_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task PublishCard_WhenAlreadyPublished_ReturnsConflict()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        var firstResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);
        var secondResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [TestMethod]
    public async Task DeclareResult_AfterPublishCard_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("ディープインパクト", DateTimeOffset.UtcNow),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task DeclareResult_BeforePublishCard_ReturnsConflict()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("ディープインパクト", DateTimeOffset.UtcNow),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task FullLifecycle_WithEntryAndPayout_ProducesCorrectState()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var entryId = $"entry-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var declaredAt = DateTimeOffset.UtcNow;

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("イクイノックス", "イクイノックス", "M", null, horseId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(16),
            JsonOptions);

        var entryResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horseId, 1, null, null, 1, 57.0m, "M", 4, 450.0m, 0.0m, entryId),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, entryResponse.StatusCode);

        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("イクイノックス", declaredAt),
            JsonOptions);

        var entryResultResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entryId}/result",
            new DeclareEntryResultRequest(1, "2:11.3", null, "35.1", null, null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, entryResultResponse.StatusCode);

        var payoutResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/payout",
            new DeclarePayoutResultRequest(
                declaredAt,
                WinPayouts: new[] { new PayoutEntryDto("1", 350m) },
                PlacePayouts: null,
                QuinellaPayouts: null,
                ExactaPayouts: null,
                TrifectaPayouts: null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, payoutResponse.StatusCode);

        var closeResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/close",
            (object?)null,
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, closeResponse.StatusCode);
    }

    [TestMethod]
    public async Task DeclareEntryResult_WhenAlreadyDeclared_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var entryId = $"entry-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("イクイノックス", "イクイノックス", "M", null, horseId, "現在の馬主"),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(16),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horseId, 1, null, null, 1, 57.0m, "M", 4, 450.0m, 0.0m, entryId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("イクイノックス", DateTimeOffset.UtcNow),
            JsonOptions);

        var first = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entryId}/result",
            new DeclareEntryResultRequest(1, "2:11.3", null, "35.1", null, null),
            JsonOptions);
        var second = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entryId}/result",
            new DeclareEntryResultRequest(1, "2:11.3", null, "35.1", null, null),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
    }

    [TestMethod]
    public async Task DeclarePayout_WhenAlreadyDeclared_ReturnsConflict()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var entryId = $"entry-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var declaredAt = DateTimeOffset.UtcNow;

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("イクイノックス", "イクイノックス", "M", null, horseId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(16),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horseId, 1, null, null, 1, 57.0m, "M", 4, 450.0m, 0.0m, entryId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("イクイノックス", declaredAt),
            JsonOptions);

        var first = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/payout",
            new DeclarePayoutResultRequest(
                declaredAt,
                WinPayouts: new[] { new PayoutEntryDto("1", 350m) },
                PlacePayouts: null,
                QuinellaPayouts: null,
                ExactaPayouts: null,
                TrifectaPayouts: null),
            JsonOptions);
        var second = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/payout",
            new DeclarePayoutResultRequest(
                declaredAt,
                WinPayouts: new[] { new PayoutEntryDto("1", 350m) },
                PlacePayouts: null,
                QuinellaPayouts: null,
                ExactaPayouts: null,
                TrifectaPayouts: null),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
    }

    [TestMethod]
    public async Task RegisterEntry_WhenRelatedSubjectsAreMissing_AutoCreatesHorseJockeyTrainer()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var entryId = $"entry-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        var trainerId = $"trainer-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2026, 5, 30), "TOKYO", 9, "自動作成テスト", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        var entryResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(
                HorseId: horseId,
                HorseNumber: 1,
                JockeyId: jockeyId,
                TrainerId: trainerId,
                GateNumber: 1,
                AssignedWeight: 57.0m,
                SexCode: "M",
                Age: 4,
                DeclaredWeight: 470.0m,
                DeclaredWeightDiff: 2.0m,
                RunningStyleCode: null,
                EntryId: entryId,
                HorseName: "テストホース",
                JockeyName: "テスト騎手",
                TrainerName: "テスト調教師"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, entryResponse.StatusCode);

        var horseResponse = await _client.GetAsync($"/api/horses/{horseId}");
        Assert.AreEqual(HttpStatusCode.OK, horseResponse.StatusCode);

        var jockeyResponse = await _client.GetAsync($"/api/jockeys/{jockeyId}");
        Assert.AreEqual(HttpStatusCode.OK, jockeyResponse.StatusCode);

        var trainerResponse = await _client.GetAsync($"/api/trainers/{trainerId}");
        Assert.AreEqual(HttpStatusCode.OK, trainerResponse.StatusCode);
    }

    [TestMethod]
    public async Task GetRace_AfterFullLifecycle_ReturnsCardWeatherTrackAndResultDetails()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var entryId = $"entry-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var observedAt = DateTimeOffset.UtcNow;

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("イクイノックス", "イクイノックス", "M", null, horseId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(16),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(
                horseId, 1, null, null, 1, 57.0m, "M", 4, 450.0m, 0.0m,
                EntryId: entryId,
                OwnerName: "レース時点の馬主"),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/weather",
            new RecordWeatherObservationRequest(observedAt, "SUNNY", "晴れ", 22.5m, 55.0m, "N", 3.2m),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/track-condition",
            new RecordTrackConditionRequest(observedAt, "GOOD", "STANDARD", "良"),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("イクイノックス", observedAt),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entryId}/result",
            new DeclareEntryResultRequest(1, "2:11.3", null, "35.1", null, 500000m),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/payout",
            new DeclarePayoutResultRequest(
                observedAt,
                WinPayouts: [new PayoutEntryDto("1", 350m)],
                PlacePayouts: [new PayoutEntryDto("1", 180m)],
                QuinellaPayouts: null,
                ExactaPayouts: null,
                TrifectaPayouts: null),
            JsonOptions);

        var response = await _client.GetAsync($"/api/races/{raceId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var race = await response.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(race);
        Assert.AreEqual(1, race.Entries.Count);
        Assert.AreEqual(entryId, race.Entries[0].EntryId);
        Assert.AreEqual(horseId, race.Entries[0].HorseId);
        Assert.AreEqual("イクイノックス", race.Entries[0].HorseName);
        Assert.AreEqual("レース時点の馬主", race.Entries[0].OwnerName);
        Assert.AreEqual(1, race.WeatherObservations.Count);
        Assert.AreEqual("晴れ", race.WeatherObservations[0].WeatherText);
        Assert.AreEqual(1, race.TrackConditionObservations.Count);
        Assert.AreEqual("良", race.TrackConditionObservations[0].GoingDescriptionText);
        Assert.AreEqual("イクイノックス", race.WinningHorseName);
        Assert.IsNotNull(race.WinningHorseId);
        Assert.IsNull(race.StewardReportText);
        Assert.AreEqual(1, race.EntryResults.Count);
        Assert.AreEqual(horseId, race.EntryResults[0].HorseId);
        Assert.AreEqual("イクイノックス", race.EntryResults[0].HorseName);
        Assert.AreEqual(1, race.EntryResults[0].FinishPosition);
        Assert.IsNotNull(race.PayoutResult);
        Assert.AreEqual(1, race.PayoutResult.WinPayouts.Count);
        Assert.AreEqual(350m, race.PayoutResult.WinPayouts[0].Amount);
        Assert.IsNotNull(race.Odds);
        Assert.IsFalse(race.Odds.IsAvailable);
    }

    [TestMethod]
    public async Task RecordWeatherObservation_AfterCreate_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "東京優駿", raceId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/weather",
            new RecordWeatherObservationRequest(DateTimeOffset.UtcNow, "SUNNY", "晴れ", 22.5m, 55.0m, "N", 3.2m),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RecordTrackCondition_AfterCreate_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "東京優駿", raceId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/track-condition",
            new RecordTrackConditionRequest(DateTimeOffset.UtcNow, "GOOD", null, "Good to Firm"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task OpenPreRaceAndStartRace_AfterPublishCard_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "東京優駿", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        var openResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/open-pre-race",
            (object?)null,
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, openResponse.StatusCode);

        var startResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/start",
            (object?)null,
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, startResponse.StatusCode);
    }

    [TestMethod]
    public async Task CorrectRaceData_AfterCreate_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "誤ったレース名", raceId),
            JsonOptions);

        var response = await _client.PatchAsJsonAsync(
            $"/api/races/{raceId}",
            new CorrectRaceDataRequest("正しいレース名", null, null, "G1", "TURF", 2400, null, "レース名の修正"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
