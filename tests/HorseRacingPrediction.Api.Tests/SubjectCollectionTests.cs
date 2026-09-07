using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SubjectCollectionTests
{
    [TestMethod]
    public async Task ProfileRefresh_PreservesIdentityAndMissingFieldsAndRejectsDifferentSubject()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app; using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var id = "horse-" + Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/horses", new RegisterHorseRequest("エンジャムメント", "エンジャムメント", "F", new DateOnly(2024, 4, 11), id, "旧馬主"))).EnsureSuccessStatusCode();
        var path = $"/api/admin/subjects/Horse/{id}/profile";
        var profile = new JraSubjectProfileDto("Horse", "エンジャムメント", "public-horse-id", "https://www.jra.go.jp/JRADB/accessU.html",
            new() { ["生年月日"] = "2024年4月11日", ["性別"] = "牝", ["馬主名"] = "新馬主", ["父"] = "父馬", ["毛色"] = "栗毛" }, DateTimeOffset.UtcNow);
        (await client.PostAsJsonAsync(path, profile)).EnsureSuccessStatusCode();
        var next = profile with { Fields = new() { ["生年月日"] = "2024年4月11日", ["毛色"] = "鹿毛" } };
        (await client.PostAsJsonAsync(path, next)).EnsureSuccessStatusCode();
        var saved = (await client.GetFromJsonAsync<JraSubjectProfileDto>(path))!;
        Assert.AreEqual("鹿毛", saved.Fields["毛色"]); Assert.AreEqual("父馬", saved.Fields["父"]);
        Assert.AreEqual("新馬主", (await client.GetFromJsonAsync<HorseProfileResponse>($"/api/horses/{id}"))!.OwnerName);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, profile with { SourceIdentity = "different" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, profile with { Fields = new() { ["生年月日"] = "2023年4月11日" } })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, profile with { Name = "別の馬" })).StatusCode);
    }

    [TestMethod]
    public async Task Requests_DeduplicateByOperationAndRetryFailedChildren()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app; using var client = http;
        var id = "horse-" + Guid.NewGuid(); var path = $"/api/admin/subjects/Horse/{id}/collection/history";
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        (await client.PostAsJsonAsync("/api/horses", new RegisterHorseRequest("テスト馬", "テスト馬", null, null, id))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(path, new { })).EnsureSuccessStatusCode();
        var first = (await client.GetFromJsonAsync<SubjectCollectionStatus>(path))!;
        (await client.PostAsJsonAsync(path, new { })).EnsureSuccessStatusCode();
        Assert.AreEqual(first.Job.JobId, (await client.GetFromJsonAsync<SubjectCollectionStatus>(path))!.Job.JobId);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        var subject = new SubjectCollectionPayload(id, "Horse", "テスト馬");
        var child = new HorseHistoryRacePayload(subject, new(2020, 1, 1), "東京", "レース", "https://www.jra.go.jp/example", "レース");
        await store.ScheduleJobAsync(AgentJobType.HorseHistoryRace, "test-child", AgentJobPayloadSerializer.Serialize(child), DateTimeOffset.UtcNow, parentJobId: first.Job.JobId);
        await store.WaitForDependenciesAsync(first.Job.JobType, first.Job.DeduplicationKey);
        await store.FailJobAsync(AgentJobType.HorseHistoryRace, "test-child", "取得失敗");
        var failed = (await client.GetFromJsonAsync<SubjectCollectionStatus>(path))!;
        Assert.AreEqual(1, failed.Failed); Assert.AreEqual(AgentJobStatus.Failed, failed.Job.Status);
        (await client.PostAsJsonAsync(path + "/retry", new { })).EnsureSuccessStatusCode();
        Assert.AreEqual(first.Job.JobId, (await client.GetFromJsonAsync<SubjectCollectionStatus>(path))!.Job.JobId);
    }

    [TestMethod]
    public async Task TrainerProfileAndRaceResolutionUseExistingRecords()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync(); await using var disposable = app; using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var trainerId = "trainer-" + Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/trainers", new RegisterTrainerRequest("中舘 英二", "中舘英二", null, trainerId))).EnsureSuccessStatusCode();
        var profile = new JraSubjectProfileDto("Trainer", "中舘 英二", "trainer-key", "https://www.jra.go.jp/JRADB/accessC.html",
            new() { ["生年月日"] = "1965年7月22日", ["所属"] = "美浦", ["免許取得年"] = "2015年" }, DateTimeOffset.UtcNow);
        (await client.PostAsJsonAsync($"/api/admin/subjects/Trainer/{trainerId}/profile", profile)).EnsureSuccessStatusCode();
        Assert.AreEqual("美浦", (await client.GetFromJsonAsync<TrainerProfileResponse>($"/api/trainers/{trainerId}"))!.AffiliationCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/admin/subjects/Trainer/{trainerId}/collection/history", new { })).StatusCode);
        var raceId = "race-" + Guid.NewGuid(); var date = new DateOnly(2026, 9, 6);
        (await client.PostAsJsonAsync("/api/races", new { raceId, raceDate = date, racecourseCode = "NAKAYAMA", raceNumber = 6, raceName = "旧名" })).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/admin/collection/horse-history/race", new PrepareHorseHistoryRaceRequest(date, "中山", 6, "メイクデビュー中山"));
        response.EnsureSuccessStatusCode();
        Assert.AreEqual(raceId, (await response.Content.ReadFromJsonAsync<RaceIdentity>())!.RaceId);
    }
    [TestMethod]
    public async Task HistoryResult_AttachesNewRaceToOriginHorseAndRejectsWrongHorse()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var application = app; using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var horseId = "horse-" + Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/horses", new RegisterHorseRequest("履歴の馬", "履歴の馬", null, null, horseId))).EnsureSuccessStatusCode();
        var date = new DateOnly(2026, 9, 6);
        var prepare = await client.PostAsJsonAsync("/api/admin/collection/horse-history/race", new PrepareHorseHistoryRaceRequest(date, "中山", 6, "メイクデビュー中山"));
        prepare.EnsureSuccessStatusCode();
        var raceId = (await prepare.Content.ReadFromJsonAsync<RaceIdentity>())!.RaceId;
        var request = new DeclareRaceResultBulkRequest(date, "中山", 6, "メイクデビュー中山", EntryCount: 1,
            Entries: [new(8, 1, "1:53.9", null, "37.6", null, 7800000, HorseName: "履歴の馬")],
            TargetRaceId: raceId, RefreshExistingData: true, SourceHorseId: horseId);
        (await client.PostAsJsonAsync("/api/races/result-bulk", request)).EnsureSuccessStatusCode();
        var race = (await client.GetFromJsonAsync<RaceResponse>($"/api/races/{raceId}"))!;
        Assert.AreEqual(horseId, race.Entries.Single().HorseId);
        Assert.AreEqual(1, race.EntryResults.Count);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/races/result-bulk", request with { SourceHorseId = "horse-" + Guid.NewGuid() })).StatusCode);
    }

    private sealed record RaceIdentity(string RaceId);
}
