using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SharedCollectionIdentityTests
{
    [TestMethod]
    public async Task CardReacquisition_CorrectsJockeyWithoutChangingHorseOrEntryIdentity()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        const string source = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002022103875/DA";
        var dirty = new DeclareRaceResultBulkRequest(new(2037, 9, 27), "中山", 11, "DOM re-extraction", EntryCount: 1, IsRaceCard: true,
            Entries: [new(2, null, null, null, null, null, null, HorseName: "アイサンサン", JockeyName: "幸 英明 106 M", HorseSourceIdentity: source)]);
        var first = await (await http.PostAsJsonAsync("/api/races/result-bulk", dirty)).Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(first);
        Assert.IsTrue(first.CorePersisted, string.Join(";", first.Errors));
        using var before = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"/api/races/{first.RaceId}"));
        var oldEntry = before.RootElement.GetProperty("entries")[0];
        var clean = dirty with { Entries = [dirty.Entries![0] with { JockeyName = "幸 英明" }] };
        var second = await (await http.PostAsJsonAsync("/api/races/result-bulk", clean)).Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(second);
        Assert.IsTrue(second.CorePersisted, string.Join(";", second.Errors));
        Assert.AreEqual(first.RaceId, second.RaceId);
        using var after = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"/api/races/{first.RaceId}"));
        var entry = after.RootElement.GetProperty("entries")[0];
        Assert.AreEqual(oldEntry.GetProperty("entryId").GetString(), entry.GetProperty("entryId").GetString());
        Assert.AreEqual(oldEntry.GetProperty("horseId").GetString(), entry.GetProperty("horseId").GetString());
        Assert.AreEqual("幸 英明", entry.GetProperty("jockeyName").GetString());
        Assert.AreNotEqual(oldEntry.GetProperty("jockeyId").GetString(), entry.GetProperty("jockeyId").GetString());
        using var oldMaster = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"/api/jockeys/{oldEntry.GetProperty("jockeyId").GetString()}"));
        Assert.AreEqual("幸 英明 106 M", oldMaster.RootElement.GetProperty("displayName").GetString());
    }

    [TestMethod]
    public async Task OwnerRecovery_RequiresUnchangedPreviewAndPause_ReplaysWithoutErasingFailure()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("owner-identity");
        await SubjectCollectionDefinitions.RegisterAsync(store);
        const string name = "(株)回復確認ＲＡＣＩＮＧ";
        var oldId = DeterministicIdGenerator.BuildEntityId("owner", DeterministicIdGenerator.NormalizeKey(name));
        var target = OwnerIdentityContract.CreateId(name);
        var card = new DeclareRaceResultBulkRequest(new(2037, 10, 2), "東京", 3, "recovery", EntryCount: 1, IsRaceCard: true,
            Entries: [new(1, null, null, null, null, null, null, HorseName: "回復確認馬", OwnerName: name)]);
        var saved = await (await http.PostAsJsonAsync("/api/races/result-bulk", card)).Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(saved);
        Assert.IsTrue(saved.CorePersisted, string.Join(";", saved.Errors));
        var now = DateTimeOffset.UtcNow;
        var source = new ResourceKey(CollectionResourceType.Owner, "JRA", oldId);
        var original = await store.RequestAsync(source, definition, 1, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = name, ["requestedByRaceId"] = saved.RaceId });
        var lease = await store.AcquireAsync(original.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(lease.TaskId, lease.LeaseToken, DateTimeOffset.UtcNow,
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "OwnerNotRegistered")));
        var unrelated = await store.RequestAsync(new(CollectionResourceType.Owner, "JRA", "owner-" + Guid.NewGuid()), definition, 1,
            CollectionReason.Discovery, now, attributes: new Dictionary<string, string> { ["name"] = name, ["requestedByRaceId"] = saved.RaceId });
        var unrelatedLease = await store.AcquireAsync(unrelated.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(unrelatedLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(unrelatedLease.TaskId, unrelatedLease.LeaseToken, DateTimeOffset.UtcNow,
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "OwnerNotRegistered")));
        var candidates = await http.GetFromJsonAsync<OwnerIdentityRecoveryCandidate[]>("/api/admin/repairs/owner-identity");
        var candidate = candidates!.Single(x => x.TaskId == original.TaskId);
        var blocked = candidates!.Single(x => x.TaskId == unrelated.TaskId);
        Assert.AreEqual("NotKnownFaultyOwnerId", blocked.BlockingReason);
        Assert.IsNull(candidate.BlockingReason);
        Assert.AreEqual(target, candidate.TargetId);
        var request = new OwnerIdentityRecoveryRequest([new(candidate.NotificationId, candidate.Fingerprint!)]);
        const string path = "/api/admin/repairs/owner-identity/execute";
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path, request)).StatusCode);
        await store.SetPausedAsync(true, "test recovery", now);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path,
            new OwnerIdentityRecoveryRequest([request.Items[0], new(blocked.NotificationId, "unrelated")]))).StatusCode);
        Assert.IsEmpty(await store.GetBatchResourceStatusesAsync($"owner-identity-contract-v1:{candidate.NotificationId:N}"));
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path,
            new OwnerIdentityRecoveryRequest([new(candidate.NotificationId, "stale")]))).StatusCode);
        var applied = await http.PostAsJsonAsync(path, request);
        applied.EnsureSuccessStatusCode();
        var first = (await applied.Content.ReadFromJsonAsync<CollectionRequestReceipt[]>())!.Single();
        var replay = await http.PostAsJsonAsync(path, request);
        replay.EnsureSuccessStatusCode();
        Assert.AreEqual(first.RequestId, (await replay.Content.ReadFromJsonAsync<CollectionRequestReceipt[]>())!.Single().RequestId);
        var history = await store.GetResourceDetailAsync(source, definition);
        Assert.AreEqual(CollectionAttemptResult.ResourceNotFound, history!.Attempts.Single().Result);
        Assert.AreEqual(CollectionFailureResolutionStatus.Superseded, history.Failures!.Single().ResolutionStatus);
        Assert.IsTrue(await new HorseRacingPrediction.Collector.CollectionPlatform.OwnerIdentityApiClient(http).ExistsAsync(target, CancellationToken.None));
        await store.SetPausedAsync(false, "test recovery complete", now);
        var recovery = await store.AcquireAsync(first.TaskId!.Value, 1, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(recovery);
        Assert.IsTrue(await store.CompleteAttemptAsync(recovery.TaskId, recovery.LeaseToken, DateTimeOffset.UtcNow, new(CollectionAttemptResult.Succeeded)));
    }

    [TestMethod]
    public async Task LegacyRace_AlternateCoursePreservesIdAndLeaseAndHoldBinding()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var id = "race-" + Guid.NewGuid();
        var date = new DateOnly(2037, 9, 27);
        (await http.PostAsJsonAsync("/api/races", new CreateRaceRequest(date, "TOKYO", 1, "旧ID", id))).EnsureSuccessStatusCode();
        foreach (var course in new[] { "東京", "Tokyo", "tokyo" })
        {
            var resolved = await http.PostAsJsonAsync("/api/identity/race", new ResolveRaceIdentityRequest(date, course, 1));
            resolved.EnsureSuccessStatusCode();
            Assert.AreEqual(id, (await resolved.Content.ReadFromJsonAsync<ResolvedIdentityDto>())!.Id);
        }
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("race-detail"), "Race", CollectionResourceType.Race, CollectionDefinitionRevisions.RaceDetail, "test", true);
        var now = DateTimeOffset.UtcNow;
        await store.RegisterDefinitionAsync(new("race-discovery"), "Discovery", CollectionResourceType.Race, 1, "test", false);
        await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "discovery:2037092700"), new("race-discovery"), 1, CollectionReason.Initial, now);
        Assert.IsFalse(await store.HasActiveRaceMutationAsync(id));
        var receipt = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "20370927:Tokyo:1"), new("race-detail"),
            CollectionDefinitionRevisions.RaceDetail, CollectionReason.Initial, now);
        Assert.IsTrue(await store.HasActiveRaceMutationAsync(id));
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken, id));
        Assert.IsFalse(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken,
            DeterministicIdGenerator.BuildRaceId(date, "東京", 1)));
        var hold = await store.HoldRaceForRepairAsync(id, Guid.NewGuid().ToString(), 0, "test", now);
        Assert.IsTrue(hold.IsActive);
        Assert.IsTrue(hold.Aliases.Contains("20370927:Tokyo:1"));
        Assert.IsFalse(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken, id));
        var resolver = app.Services.GetRequiredService<IRaceResourceIdentityResolver>();
        Assert.IsNull(resolver.Resolve("20370927:Tokyo:1", new Dictionary<string, string> { ["domainRaceId"] = "race-" + Guid.NewGuid() }));
    }

    [TestMethod]
    public async Task DuplicateRace_IsRejectedWithoutSelectingAnArbitraryExistingId()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var date = new DateOnly(2037, 10, 1);
        foreach (var course in new[] { "東京", "TOKYO" })
            (await http.PostAsJsonAsync("/api/races", new CreateRaceRequest(date, course, 2, "duplicate", "race-" + Guid.NewGuid()))).EnsureSuccessStatusCode();
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/identity/race", new ResolveRaceIdentityRequest(date, "Tokyo", 2))).StatusCode);
    }

    [TestMethod]
    public async Task HorseFallback_PreservesLegacyIdAndRejectsAmbiguityAndUnprovenPromotion()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        const string raw = "マル外 ＡＢＣ";
        var legacy = DeterministicIdGenerator.BuildEntityId("horse", DeterministicIdGenerator.NormalizeKey(raw));
        (await http.PostAsJsonAsync("/api/horses", new RegisterHorseRequest(raw, raw, "M", new(2024, 1, 1), legacy))).EnsureSuccessStatusCode();
        Assert.AreEqual(DeterministicIdGenerator.BuildHorseId(raw), DeterministicIdGenerator.BuildHorseId("ABC"));
        var resolved = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("ABC", BirthDate: new(2024, 1, 1)));
        resolved.EnsureSuccessStatusCode();
        Assert.AreEqual(legacy, (await resolved.Content.ReadFromJsonAsync<ResolvedIdentityDto>())!.Id);
        (await http.PutAsJsonAsync($"/api/horses/{legacy}", new UpdateHorseProfileRequest("ABC", "ABC", "M", new(2024, 1, 1)))).EnsureSuccessStatusCode();
        var afterNameCorrection = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("ABC"));
        afterNameCorrection.EnsureSuccessStatusCode();
        Assert.AreEqual(legacy, (await afterNameCorrection.Content.ReadFromJsonAsync<ResolvedIdentityDto>())!.Id);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("ABC", BirthDate: new(2023, 1, 1)))).StatusCode);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("ABC", "https://www.jra.go.jp/JRADB/accessU.html?CNAME=other"))).StatusCode);
        (await http.PostAsJsonAsync("/api/horses", new RegisterHorseRequest("ABC", "ABC", "M", new(2020, 1, 1), DeterministicIdGenerator.BuildHorseId("ABC")))).EnsureSuccessStatusCode();
        var ambiguous = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("ABC"));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, ambiguous.StatusCode);
        Assert.AreEqual("AmbiguousHorseIdentity", (await ambiguous.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["code"]);
        var collisionId = DeterministicIdGenerator.BuildHorseId("ABC-DEF");
        (await http.PostAsJsonAsync("/api/horses", new RegisterHorseRequest("ABC-DEF", "ABC-DEF", null, null, collisionId))).EnsureSuccessStatusCode();
        Assert.AreEqual(collisionId, DeterministicIdGenerator.BuildHorseId("ABCDEF"));
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("ABCDEF"))).StatusCode);
    }

    [TestMethod]
    public async Task HorseProfile_EquivalentOfficialUrlsResolveLegacyId_ButNameOnlyAndDifferentSourceAreRejected()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var id = "horse-" + Guid.NewGuid();
        const string source = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024123456/00";
        (await http.PostAsJsonAsync("/api/horses", new RegisterHorseRequest("サンプル", "サンプル", "M", new(2024, 1, 1), id))).EnsureSuccessStatusCode();
        var path = $"/api/v2/admin/subjects/Horse/{id}/profile";
        var profile = new JraSubjectProfileDto("Horse", "マル外 サンプル", source, source,
            new() { ["生年月日"] = "2024年1月1日" }, DateTimeOffset.UtcNow);
        (await http.PutAsJsonAsync(path, profile)).EnsureSuccessStatusCode();
        (await http.PutAsJsonAsync(path, profile with { SourceIdentity = source + "&extra=1" })).EnsureSuccessStatusCode();
        var resolved = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("サンプル", source));
        resolved.EnsureSuccessStatusCode();
        Assert.AreEqual(id, (await resolved.Content.ReadFromJsonAsync<ResolvedIdentityDto>())!.Id);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("サンプル"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PutAsJsonAsync(path, profile with { SourceIdentity = source.Replace("123456", "654321"), SourceUrl = source.Replace("123456", "654321") })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync(path, profile with { SourceUrl = source.Replace("www.jra.go.jp", "example.com") })).StatusCode);
    }
}
