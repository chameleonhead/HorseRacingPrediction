using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Shared = HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class RaceReacquisitionTests
{
    private static readonly DateOnly Date = new(2026, 9, 6);

    [TestMethod]
    public async Task Requests_DeduplicateActiveRaceAndAllowNewRequestAfterCompletion()
    {
        // 本番と同様に要求ごとに別SQLite接続を使う。単一:memory:接続は同時クエリを処理できない。
        var database = Path.Combine(Path.GetTempPath(), "hrp-concurrent-" + Guid.NewGuid().ToString("N") + ".db");
        var (app, client) = await TestApplicationFactory.CreateAsync("Data Source=" + database);
        await using var disposable = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = "race-" + Guid.NewGuid();
        (await http.PostAsJsonAsync("/api/races", new { raceId, raceDate = Date, racecourseCode = "中山", raceNumber = 6, raceName = "誤った名称" })).EnsureSuccessStatusCode();
        var path = $"/api/admin/races/{raceId}/reacquisition";
        Assert.AreEqual(HttpStatusCode.NoContent, (await http.GetAsync(path)).StatusCode);
        var requests = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => http.PostAsJsonAsync(path, new { })));
        foreach (var response in requests) Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var ids = await Task.WhenAll(requests.Select(x => x.Content.ReadFromJsonAsync<RequestResult>()));
        Assert.AreEqual(1, ids.Select(x => x!.JobId).Distinct().Count());
        var status = (await http.GetFromJsonAsync<AgentJobDetailReadModel>(path))!;
        Assert.AreEqual(AgentJobStatus.Ready, status.Status);
        Assert.AreEqual(raceId, AgentJobPayloadSerializer.Deserialize<RaceReacquisitionPayload>(status.Payload).RaceId);
        Assert.AreEqual("ReacquireRace", status.AuditHistory.Single().Operation);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        await store.CompleteJobAsync(status.JobType, status.DeduplicationKey);
        var next = await http.PostAsJsonAsync(path, new { });
        Assert.AreNotEqual(status.JobId, (await next.Content.ReadFromJsonAsync<RequestResult>())!.JobId);
    }

    [TestMethod]
    public async Task Request_RequiresAuthenticationAndExistingJraRace()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var http = client;
        var raceId = "race-" + Guid.NewGuid();
        var path = $"/api/admin/races/{raceId}/reacquisition";
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(path, new { })).StatusCode);
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        Assert.AreEqual(HttpStatusCode.NotFound, (await http.PostAsJsonAsync(path, new { })).StatusCode);
        (await http.PostAsJsonAsync("/api/races", new { raceId, raceDate = Date, racecourseCode = "地方", raceNumber = 6, raceName = "地方レース" })).EnsureSuccessStatusCode();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path, new { })).StatusCode);
    }

    [TestMethod]
    public async Task Refresh_UpdatesClosedRaceAndAllPayoutsWithoutDuplicatesOrErasingMissingFields()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = "race-" + Guid.NewGuid();
        (await http.PostAsJsonAsync("/api/races", new { raceId, raceDate = Date, racecourseCode = "中山", raceNumber = 6, raceName = "誤った名称" })).EnsureSuccessStatusCode();
        var entry = new Shared.RaceResultEntryBulkDto(8, 1, "1:53.9", null, "37.6", null, 7800000,
            HorseName: "エンジャムメント", JockeyName: "旧騎手", TrainerName: "調教師", GateNumber: 6,
            AssignedWeight: 55m, SexCode: "F", Age: 2, BodyWeight: 498, Popularity: 1, OwnerName: "馬主", CornerPositions: "2 2 2 2");
        var payouts = new Shared.DeclarePayoutResultRequest(DateTimeOffset.UtcNow, [new("8", 310)], [new("8", 140)],
            [new("7-8", 840)], [new("8-7", 1640)], [new("8-7-12", 8240)], [new("6-6", 770)], [new("7-8", 340)], [new("7-8-12", 2270)]);
        var request = new Shared.DeclareRaceResultBulkRequest(Date, "中山", 6, "旧レース名", EntryCount: 1,
            GradeCode: "G3", SurfaceCode: "ダート", DistanceMeters: 1800, WinningHorseName: "旧勝馬",
            Entries: [entry], Payouts: payouts, TargetRaceId: raceId, RefreshExistingData: true);
        (await http.PostAsJsonAsync("/api/races/result-bulk", request)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync($"/api/races/{raceId}/close", new { })).EnsureSuccessStatusCode();
        var updated = request with { RaceName = "メイクデビュー中山", GradeCode = null, DistanceMeters = 1900,
            WinningHorseName = "エンジャムメント", Entries = [entry with { JockeyName = "新騎手", AssignedWeight = 54m, BodyWeight = 500, OwnerName = null, OfficialTime = "1:53.8", Popularity = 2 }],
            StartTime = new TimeOnly(12, 55), OverallPaceText = "12.5 - 11.4", CornerPassagesText = "4角 8,7,12", CourseLayout = "内",
            Payouts = payouts with { WinPayouts = [new("8", 320)], PlacePayouts = null, WidePayouts = [new("7-8", 350)] } };
        for (var i = 0; i < 2; i++) (await http.PostAsJsonAsync("/api/races/result-bulk", updated)).EnsureSuccessStatusCode();
        var race = (await http.GetFromJsonAsync<RaceResponse>($"/api/races/{raceId}"))!;
        Assert.AreEqual("メイクデビュー中山", race.RaceName);
        Assert.AreEqual(Shared.RaceStatus.Closed, race.Status);
        Assert.AreEqual("G3", race.GradeCode);
        Assert.AreEqual(1900, race.DistanceMeters);
        Assert.AreEqual(new TimeOnly(12, 55), race.StartTime);
        Assert.AreEqual("12.5 - 11.4", race.OverallPaceText);
        Assert.AreEqual("4角 8,7,12", race.CornerPassagesText);
        Assert.AreEqual("内", race.CourseLayout);
        Assert.AreEqual("エンジャムメント", race.WinningHorseName);
        Assert.AreEqual(1, race.Entries.Count);
        Assert.AreEqual("新騎手", race.Entries.Single().JockeyName);
        Assert.AreEqual(54m, race.Entries.Single().AssignedWeight);
        Assert.AreEqual("馬主", race.Entries.Single().OwnerName);
        Assert.AreEqual(1, race.EntryResults.Count);
        Assert.AreEqual("1:53.8", race.EntryResults.Single().OfficialTime);
        Assert.AreEqual(2, race.EntryResults.Single().Popularity);
        Assert.AreEqual("2 2 2 2", race.EntryResults.Single().CornerPositions);
        Assert.AreEqual(320m, race.PayoutResult!.WinPayouts.Single().Amount);
        Assert.AreEqual(140m, race.PayoutResult.PlacePayouts.Single().Amount);
        Assert.AreEqual(350m, race.PayoutResult.WidePayouts!.Single().Amount);
        Assert.AreEqual(2270m, race.PayoutResult.TrioPayouts!.Single().Amount);
        Assert.AreEqual(770m, race.PayoutResult.BracketQuinellaPayouts!.Single().Amount);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/races/result-bulk", updated with { RaceNumber = 7 })).StatusCode);
    }

    [TestMethod]
    public async Task Refresh_PreservesExistingSubjectIdsAndEntryIdsForMatchingNames()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = "race-" + Guid.NewGuid();
        var horseId = "horse-" + Guid.NewGuid();
        var jockeyId = "jockey-" + Guid.NewGuid();
        var trainerId = "trainer-" + Guid.NewGuid();
        (await http.PostAsJsonAsync("/api/races", new { raceId, raceDate = Date, racecourseCode = "中山", raceNumber = 6, raceName = "旧名称" })).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync($"/api/races/{raceId}/card/publish", new { entryCount = 1 })).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync($"/api/races/{raceId}/entries", new RegisterEntryRequest(horseId, 8, jockeyId, trainerId, null, null, null, null, null, null,
            EntryId: "manual-entry", HorseName: "テストホース", JockeyName: "Ｃ．ルメール", TrainerName: "田中 太郎"))).EnsureSuccessStatusCode();
        var request = new Shared.DeclareRaceResultBulkRequest(Date, "中山", 6, "メイクデビュー中山", EntryCount: 1,
            Entries: [new(8, null, null, null, null, null, null, HorseName: "テストホース", JockeyName: "Ｃ．ルメール", TrainerName: "田中太郎")],
            TargetRaceId: raceId, RefreshExistingData: true, IsRaceCard: true);
        (await http.PostAsJsonAsync("/api/races/result-bulk", request)).EnsureSuccessStatusCode();
        var race = (await http.GetFromJsonAsync<RaceResponse>($"/api/races/{raceId}"))!;
        var entry = race.Entries.Single();
        Assert.AreEqual("manual-entry", entry.EntryId);
        Assert.AreEqual(horseId, entry.HorseId);
        Assert.AreEqual(jockeyId, entry.JockeyId);
        Assert.AreEqual(trainerId, entry.TrainerId);
    }

    private sealed record RequestResult(string JobId);
}
