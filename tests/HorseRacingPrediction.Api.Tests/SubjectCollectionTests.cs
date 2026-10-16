using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Races;
using HorseRacingPrediction.Contracts.Subjects;
using HorseRacingPrediction.Contracts.Trainers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SubjectCollectionTests
{
    [TestMethod]
    public async Task ProfileLeasePair_RequiresLiveOwnershipAndAllowsHeaderlessManualSave()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app; using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var horseId = "horse-" + Guid.NewGuid().ToString("D").ToLowerInvariant();
        (await client.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse("リース馬", "リース馬", "F", null, horseId, null))).EnsureSuccessStatusCode();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", CollectionResourceType.Horse, 3, "test", true);
        var now = DateTimeOffset.UtcNow;
        var requested = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", horseId), definition, 3,
            CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(requested.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        var sourceUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=profile-lease";
        var profile = new JraSubjectProfileDto("Horse", "リース馬", sourceUrl,
            sourceUrl,
            new() { ["生年月日"] = "2024年4月11日", ["毛色"] = "鹿毛" }, now);
        var path = $"/api/v2/admin/subjects/Horse/{horseId}/profile";
        using var leasedRequest = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(new PutSubjectProfileRequest("Horse", horseId, profile)),
        };
        leasedRequest.Headers.Add("X-Collection-Task-Id", lease.TaskId.ToString("D"));
        leasedRequest.Headers.Add("X-Collection-Lease-Token", lease.LeaseToken);
        (await client.SendAsync(leasedRequest)).EnsureSuccessStatusCode();
        Assert.IsNotNull(await client.GetFromJsonAsync<GetSubjectProfileResponse>(
            $"/api/v2/admin/subjects/Horse/{horseId}/profiles/current"));

        var manualId = "horse-" + Guid.NewGuid().ToString("D").ToLowerInvariant();
        (await client.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse("手動馬", "手動馬", "F", null, manualId, null))).EnsureSuccessStatusCode();
        var manualProfile = profile with { Name = "手動馬", SourceIdentity = sourceUrl };
        (await client.PutAsJsonAsync($"/api/v2/admin/subjects/Horse/{manualId}/profile",
            new PutSubjectProfileRequest("Horse", manualId, manualProfile))).EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task ProfileLeasePair_InvalidOrCrossResourceHeadersRejectBeforeProfileWrite()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app; using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var firstHorse = "horse-" + Guid.NewGuid().ToString("D").ToLowerInvariant();
        var targetHorse = "horse-" + Guid.NewGuid().ToString("D").ToLowerInvariant();
        foreach (var id in new[] { firstHorse, targetHorse })
            (await client.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse("対象馬", "対象馬", "F", null, id, null))).EnsureSuccessStatusCode();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", CollectionResourceType.Horse, 3, "test", true);
        var now = DateTimeOffset.UtcNow;
        var requested = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", firstHorse), definition, 3,
            CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(requested.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        var profile = new JraSubjectProfileDto("Horse", "対象馬", "source-lease", "https://www.jra.go.jp/JRADB/accessU.html",
            new() { ["生年月日"] = "2024年4月11日" }, now);

        using var partial = new HttpRequestMessage(HttpMethod.Put, $"/api/v2/admin/subjects/Horse/{firstHorse}/profile")
        { Content = JsonContent.Create(new PutSubjectProfileRequest("Horse", firstHorse, profile)) };
        partial.Headers.Add("X-Collection-Task-Id", lease.TaskId.ToString("D"));
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.SendAsync(partial)).StatusCode);

        using var malformed = new HttpRequestMessage(HttpMethod.Put, $"/api/v2/admin/subjects/Horse/{firstHorse}/profile")
        { Content = JsonContent.Create(new PutSubjectProfileRequest("Horse", firstHorse, profile)) };
        malformed.Headers.Add("X-Collection-Task-Id", "not-a-guid");
        malformed.Headers.Add("X-Collection-Lease-Token", lease.LeaseToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.SendAsync(malformed)).StatusCode);

        using var crossResource = new HttpRequestMessage(HttpMethod.Put, $"/api/v2/admin/subjects/Horse/{targetHorse}/profile")
        { Content = JsonContent.Create(new PutSubjectProfileRequest("Horse", targetHorse, profile)) };
        crossResource.Headers.Add("X-Collection-Task-Id", lease.TaskId.ToString("D"));
        crossResource.Headers.Add("X-Collection-Lease-Token", lease.LeaseToken);
        var server = (TestServer)app.Services.GetRequiredService<IServer>();
        using var leaseAwareClient = new HttpClient(new CollectionWorkerLeaseHandler { InnerHandler = server.CreateHandler() })
        { BaseAddress = client.BaseAddress };
        leaseAwareClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var stale = await Assert.ThrowsExactlyAsync<StaleCollectionTaskLeaseException>(async () =>
        {
            using var response = await leaseAwareClient.SendAsync(crossResource);
        });
        var classified = CollectionAttemptFailureClassifier.FromException(stale);
        Assert.AreEqual(CollectionAttemptResult.TransientFailure, classified.Result);
        Assert.AreEqual("StaleSubjectProfileLease", classified.ErrorCode);
        Assert.AreEqual(CollectionFailureImpact.Isolated, classified.FailureImpact);

        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/v2/admin/subjects/Horse/{firstHorse}/profiles/current")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/v2/admin/subjects/Horse/{targetHorse}/profiles/current")).StatusCode);
    }

    [TestMethod]
    public async Task ProfileRefresh_PreservesIdentityAndMissingFieldsAndRejectsDifferentSubject()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app; using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var id = "horse-" + Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse("エンジャムメント", "エンジャムメント", "F", new DateOnly(2024, 4, 11), id, "旧馬主"))).EnsureSuccessStatusCode();
        var path = $"/api/v2/admin/subjects/Horse/{id}/profile";
        const string horseUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=public-horse-id";
        var profile = new JraSubjectProfileDto("Horse", "エンジャムメント", horseUrl, horseUrl,
            new() { ["生年月日"] = "2024年4月11日", ["性別"] = "牝", ["馬主名"] = "新馬主", ["生産牧場"] = "新生産者", ["父"] = "父馬", ["母"] = "母馬(母の父：母父馬)", ["毛色"] = "栗毛" }, DateTimeOffset.UtcNow);
        (await client.PutAsJsonAsync(path, new PutSubjectProfileRequest("Horse", id, profile))).EnsureSuccessStatusCode();
        var next = profile with { Fields = new() { ["生年月日"] = "2024年4月11日", ["毛色"] = "鹿毛" } };
        (await client.PutAsJsonAsync(path, new PutSubjectProfileRequest("Horse", id, next))).EnsureSuccessStatusCode();
        var saved = (await client.GetFromJsonAsync<GetSubjectProfileResponse>($"/api/v2/admin/subjects/Horse/{id}/profiles/current"))!.Profile;
        Assert.AreEqual("鹿毛", saved.Fields["毛色"]); Assert.AreEqual("父馬", saved.Fields["父"]);
        var horse = (await client.GetFromJsonAsync<GetHorseProfileResponse>($"/api/horses/{id}"))!.Horse;
        Assert.AreEqual("新馬主", horse.OwnerName);
        Assert.AreEqual("新生産者", horse.BreederName);
        Assert.AreEqual("父馬", horse.SireName);
        Assert.AreEqual("母馬", horse.DamName);
        Assert.AreEqual("母父馬", horse.DamsireName);
        Assert.AreEqual("鹿毛", horse.CoatColor);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, new PutSubjectProfileRequest("Horse", id,
            profile with { SourceIdentity = horseUrl + "different", SourceUrl = horseUrl + "different" }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, new PutSubjectProfileRequest("Horse", id,
            profile with { Fields = new() { ["生年月日"] = "2023年4月11日" } }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path,
            new PutSubjectProfileRequest("Horse", id, profile with { Name = "別の馬" }))).StatusCode);
    }

    [TestMethod]
    public async Task TrainerProfileAndRaceResolutionUseExistingRecords()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync(); await using var disposable = app; using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var trainerId = "trainer-" + Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/trainers", SubjectRequestFactory.RegisterTrainer("中舘 英二", "中舘英二", null, trainerId))).EnsureSuccessStatusCode();
        var profile = new JraSubjectProfileDto("Trainer", "中舘 英二", "trainer-key", "https://www.jra.go.jp/JRADB/accessC.html",
            new() { ["生年月日"] = "1965年7月22日", ["所属"] = "美浦", ["免許取得年"] = "2015年" }, DateTimeOffset.UtcNow);
        (await client.PutAsJsonAsync($"/api/v2/admin/subjects/Trainer/{trainerId}/profile", new PutSubjectProfileRequest("Trainer", trainerId, profile))).EnsureSuccessStatusCode();
        Assert.AreEqual("美浦", (await client.GetFromJsonAsync<GetTrainerProfileResponse>($"/api/trainers/{trainerId}"))!.Trainer.AffiliationCode);
        var raceId = "race-" + Guid.NewGuid(); var date = new DateOnly(2026, 9, 6);
        (await client.PostAsJsonAsync("/api/races", new CreateRaceRequest(new(date, "NAKAYAMA", 6, "旧名", raceId)))).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/v2/admin/races",
            new CreateRaceFromScheduleRequest(new(date, "中山", 6, "メイクデビュー中山")));
        response.EnsureSuccessStatusCode();
        Assert.AreEqual(raceId, (await response.Content.ReadFromJsonAsync<RaceIdentity>())!.RaceId);
    }

    [TestMethod]
    public async Task TrainerProfile_AcceptsCanonicalNameWhenStoredNameContainsAffiliation()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var trainerId = "trainer-" + Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/trainers",
            SubjectRequestFactory.RegisterTrainer("黒岩 陽一（美浦）", "黒岩陽一", "美浦", trainerId)))
            .EnsureSuccessStatusCode();
        var profile = new JraSubjectProfileDto("Trainer", "黒岩 陽一",
            "Trainer:黒岩陽一:1980-03-27", "https://www.jra.go.jp/JRADB/accessC.html",
            new() { ["生年月日"] = "1980年3月27日", ["所属"] = "美浦" }, DateTimeOffset.UtcNow);

        var response = await client.PutAsJsonAsync(
            $"/api/v2/admin/subjects/Trainer/{trainerId}/profile", new PutSubjectProfileRequest("Trainer", trainerId, profile));

        response.EnsureSuccessStatusCode();
        var saved = await client.GetFromJsonAsync<GetSubjectProfileResponse>(
            $"/api/v2/admin/subjects/Trainer/{trainerId}/profiles/current");
        Assert.IsNotNull(saved);
        Assert.AreEqual("黒岩 陽一", saved.Profile.Name);
    }
    [TestMethod]
    public async Task HistoryResult_AttachesNewRaceToOriginHorseAndRejectsWrongHorse()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var application = app; using var client = http;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var horseId = DeterministicIdGenerator.BuildHorseId("履歴の馬");
        (await client.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse("履歴の馬", "履歴の馬", null, null, horseId))).EnsureSuccessStatusCode();
        var date = new DateOnly(2026, 9, 6);
        var prepare = await client.PostAsJsonAsync("/api/v2/admin/races",
            new CreateRaceFromScheduleRequest(new(date, "中山", 6, "メイクデビュー中山")));
        prepare.EnsureSuccessStatusCode();
        var raceId = (await prepare.Content.ReadFromJsonAsync<RaceIdentity>())!.RaceId;
        var request = new DeclareRaceResultBulkRequest(new(date, "中山", 6, "メイクデビュー中山", EntryCount: 1,
            Entries: [new(8, 1, "1:53.9", null, "37.6", null, 7800000, HorseName: "履歴の馬")],
            TargetRaceId: raceId, RefreshExistingData: true, SourceHorseId: horseId));
        var result = await client.PostAsJsonAsync("/api/races/result-bulk", request);
        result.EnsureSuccessStatusCode();
        Assert.IsTrue((await result.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>())!.Result.CorePersisted);
        var race = (await client.GetFromJsonAsync<GetRaceResponse>($"/api/races/{raceId}"))!.Race;
        Assert.AreEqual(horseId, race.Entries.Single().HorseId);
        Assert.AreEqual(1, race.EntryResults.Count);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/races/result-bulk", request with { Result = request.Result! with { SourceHorseId = "horse-" + Guid.NewGuid() } })).StatusCode);
    }

    private sealed record RaceIdentity(string RaceId);
}
