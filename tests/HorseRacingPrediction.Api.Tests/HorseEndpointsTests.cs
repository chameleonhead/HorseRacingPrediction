using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class HorseEndpointsTests
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
    public async Task RegisterHorse_ReturnsCreated()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        var request = new RegisterHorseRequest("ディープインパクト", "deepimpact", "M", new DateOnly(2002, 3, 25), horseId);

        var response = await _client.PostAsJsonAsync("/api/horses", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task RegisterHorse_ThenGetProfile_ReturnsCorrectData()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("オルフェーヴル", "orfevr", "M", null, horseId, "サンデーレーシング"),
            JsonOptions);

        var response = await _client.GetAsync($"/api/horses/{horseId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<HorseProfileResponse>(JsonOptions);
        Assert.IsNotNull(profile);
        Assert.AreEqual(horseId, profile.HorseId);
        Assert.AreEqual("オルフェーヴル", profile.RegisteredName);
        Assert.AreEqual("orfevr", profile.NormalizedName);
        Assert.AreEqual("サンデーレーシング", profile.OwnerName);
    }

    [TestMethod]
    public async Task SearchHorses_FiltersSortsAndPages()
    {
        var key = Guid.NewGuid().ToString("N");
        var horseId1 = $"horse-{Guid.NewGuid()}";
        var horseId2 = $"horse-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest($"SearchHorseA-{key}", $"searchhorse-a-{key}", "M", null, horseId1),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest($"SearchHorseB-{key}", $"searchhorse-b-{key}", "M", null, horseId2),
            JsonOptions);

        var response = await _client.GetAsync($"/api/horses?query=SearchHorse&normalizedName={key}&page=2&pageSize=1&sortBy=registeredName&sortDescending=false");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<HorseSummaryResponse>>(JsonOptions);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual(2, result.TotalPages);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(horseId2, result.Items[0].HorseId);
    }

    [TestMethod]
    public async Task UpdateHorseProfile_AfterRegister_ReturnsOk()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("テスト馬", "testuma", null, null, horseId),
            JsonOptions);

        var response = await _client.PutAsJsonAsync(
            $"/api/horses/{horseId}",
            new UpdateHorseProfileRequest(null, "testuma-updated", "F", null),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task MergeHorseAlias_AfterRegister_ReturnsOk()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("キタサンブラック", "kitasanblack", "M", null, horseId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/horses/{horseId}/aliases",
            new MergeAliasRequest("JRA", "1234567890", "JRA-DATA", true),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task CorrectHorseData_AfterRegister_ReturnsOk()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("テスト馬", "testuma", null, null, horseId),
            JsonOptions);

        var response = await _client.PatchAsJsonAsync(
            $"/api/horses/{horseId}",
            new CorrectHorseDataRequest(null, "testuma-fixed", "M", null, "データ誤り修正"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetHorseRaceHistory_AfterRaceResultDeclared_ListsPastRace()
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
            new RegisterHorseRequest("レースヒストリーテスト号", "racehistorytest", "M", null, horseId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(16),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(
                horseId,
                1,
                null,
                null,
                1,
                57.0m,
                "M",
                4,
                450.0m,
                0.0m,
                EntryId: entryId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("レースヒストリーテスト号", DateTimeOffset.UtcNow),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entryId}/result",
            new DeclareEntryResultRequest(1, "2:31.5", null, "34.0", null, 12000m),
            JsonOptions);

        var response = await _client.GetAsync($"/api/horses/{horseId}/race-history");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var history = await response.Content.ReadFromJsonAsync<HorseRaceHistoryReadModel>(JsonOptions);
        Assert.IsNotNull(history);
        Assert.AreEqual(horseId, history.HorseId);
        Assert.AreEqual(1, history.Entries.Count);
        Assert.AreEqual(raceId, history.Entries[0].RaceId);
        Assert.AreEqual(entryId, history.Entries[0].EntryId);
        Assert.AreEqual(1, history.Entries[0].FinishPosition);

        var participationResponse = await _client.GetAsync($"/api/horses/{horseId}/participations");
        Assert.AreEqual(HttpStatusCode.OK, participationResponse.StatusCode);
        var participations = await participationResponse.Content.ReadFromJsonAsync<ParticipationHistoryResponse>(JsonOptions);
        Assert.IsNotNull(participations);
        Assert.AreEqual("Horse", participations.SubjectType);
        Assert.AreEqual("レースヒストリーテスト号", participations.Entries.Single().HorseName);
        Assert.AreEqual(1, participations.Entries.Single().FinishPosition);
    }

    [TestMethod]
    public async Task GetHorseRaceHistory_ForUnknownHorse_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/horses/horse-{Guid.NewGuid()}/race-history");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        var participationResponse = await _client.GetAsync($"/api/horses/horse-{Guid.NewGuid()}/participations");
        Assert.AreEqual(HttpStatusCode.NotFound, participationResponse.StatusCode);
    }

    [TestMethod]
    public async Task Owners_ListAndUnknownDetail_ReturnExpectedStatuses()
    {
        var listResponse = await _client.GetAsync("/api/owners");
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var detailResponse = await _client.GetAsync("/api/owners/unknown-owner");
        Assert.AreEqual(HttpStatusCode.NotFound, detailResponse.StatusCode);
    }

    [TestMethod]
    public async Task MergeOwner_CombinesAliasesAndRecordsAudit()
    {
        var key = Guid.NewGuid().ToString("N");
        await _client.PostAsJsonAsync("/api/horses", new RegisterHorseRequest($"OwnerHorseA-{key}", $"owner-horse-a-{key}", "M", null, $"horse-{Guid.NewGuid()}", $"統合先馬主{key}"), JsonOptions);
        await _client.PostAsJsonAsync("/api/horses", new RegisterHorseRequest($"OwnerHorseB-{key}", $"owner-horse-b-{key}", "F", null, $"horse-{Guid.NewGuid()}", $"統合元馬主{key}"), JsonOptions);
        var owners = await _client.GetFromJsonAsync<IReadOnlyList<OwnerSummaryResponse>>($"/api/owners?query={key}", JsonOptions);
        Assert.IsNotNull(owners);
        var target = owners.Single(x => x.DisplayName.StartsWith("統合先馬主", StringComparison.Ordinal));
        var source = owners.Single(x => x.DisplayName.StartsWith("統合元馬主", StringComparison.Ordinal));

        var merge = await _client.PostAsJsonAsync($"/api/owners/{target.OwnerId}/merge", new MergeOwnerRequest(source.OwnerId, "表記が同一人物と確認できたため"), JsonOptions);
        var detail = await _client.GetFromJsonAsync<OwnerDetailResponse>($"/api/owners/{target.OwnerId}", JsonOptions);

        Assert.AreEqual(HttpStatusCode.NoContent, merge.StatusCode);
        Assert.IsNotNull(detail);
        Assert.AreEqual(2, detail.Summary.NameVariants.Count);
        Assert.IsTrue(detail.MergeHistory.Any(x => x.SourceOwnerId == source.OwnerId && x.Reason.Contains("同一人物", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task UpdateOwner_ChangesDisplayName()
    {
        var key = Guid.NewGuid().ToString("N");
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest($"OwnerEditHorse-{key}", $"owner-edit-horse-{key}", "M", null, $"horse-{Guid.NewGuid()}", $"更新前馬主{key}"),
            JsonOptions);
        var owners = await _client.GetFromJsonAsync<IReadOnlyList<OwnerSummaryResponse>>($"/api/owners?query={key}", JsonOptions);
        Assert.IsNotNull(owners);
        var owner = owners.Single();

        var update = await _client.PutAsJsonAsync(
            $"/api/owners/{owner.OwnerId}",
            new UpdateOwnerRequest($"更新後馬主{key}", "公式表記の訂正"),
            JsonOptions);
        var detail = await _client.GetFromJsonAsync<OwnerDetailResponse>($"/api/owners/{owner.OwnerId}", JsonOptions);

        Assert.AreEqual(HttpStatusCode.NoContent, update.StatusCode);
        Assert.IsNotNull(detail);
        Assert.AreEqual($"更新後馬主{key}", detail.Summary.DisplayName);
        Assert.IsTrue(detail.Summary.NameVariants.Contains($"更新前馬主{key}"));
    }
}
