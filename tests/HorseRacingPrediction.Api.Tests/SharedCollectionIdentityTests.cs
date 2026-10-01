using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Contracts.Jockeys;
using HorseRacingPrediction.Contracts.Owners;
using HorseRacingPrediction.Contracts.Races;
using HorseRacingPrediction.Contracts.Subjects;

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
        var dirty = new DeclareRaceResultBulkRequest(new(new(2037, 9, 27), "中山", 11, "DOM re-extraction", EntryCount: 1, IsRaceCard: true,
            Entries: [new(2, null, null, null, null, null, null, HorseName: "アイサンサン", JockeyName: "幸 英明 106 M", HorseSourceIdentity: source)]));
        var first = await (await http.PostAsJsonAsync("/api/races/result-bulk", dirty)).Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(first);
        Assert.IsTrue(first.Result.CorePersisted, string.Join(";", first.Result.Errors));
        var before = await http.GetFromJsonAsync<GetRaceResponse>($"/api/races/{first.Result.RaceId}");
        Assert.IsNotNull(before);
        var oldEntry = before.Race.Entries[0];
        var clean = dirty with { Result = dirty.Result! with { Entries = [dirty.Result!.Entries![0] with { JockeyName = "幸 英明" }] } };
        var second = await (await http.PostAsJsonAsync("/api/races/result-bulk", clean)).Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(second);
        Assert.IsTrue(second.Result.CorePersisted, string.Join(";", second.Result.Errors));
        Assert.AreEqual(first.Result.RaceId, second.Result.RaceId);
        var after = await http.GetFromJsonAsync<GetRaceResponse>($"/api/races/{first.Result.RaceId}");
        Assert.IsNotNull(after);
        var entry = after.Race.Entries[0];
        Assert.AreEqual(oldEntry.EntryId, entry.EntryId);
        Assert.AreEqual(oldEntry.HorseId, entry.HorseId);
        Assert.AreEqual("幸 英明", entry.JockeyName);
        Assert.AreNotEqual(oldEntry.JockeyId, entry.JockeyId);
        var oldMaster = await http.GetFromJsonAsync<GetJockeyProfileResponse>($"/api/jockeys/{oldEntry.JockeyId}");
        Assert.AreEqual("幸 英明 106 M", oldMaster!.Jockey.DisplayName);
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
        var card = new DeclareRaceResultBulkRequest(new(new(2037, 10, 2), "東京", 3, "recovery", EntryCount: 1, IsRaceCard: true,
            Entries: [new(1, null, null, null, null, null, null, HorseName: "回復確認馬", OwnerName: name)]));
        var saved = await (await http.PostAsJsonAsync("/api/races/result-bulk", card)).Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(saved);
        Assert.IsTrue(saved.Result.CorePersisted, string.Join(";", saved.Result.Errors));
        var now = DateTimeOffset.UtcNow;
        var source = new ResourceKey(CollectionResourceType.Owner, "JRA", oldId);
        var original = await store.RequestAsync(source, definition, 1, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = name, ["requestedByRaceId"] = saved.Result.RaceId });
        var lease = await store.AcquireAsync(original.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(lease.TaskId, lease.LeaseToken, DateTimeOffset.UtcNow,
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "OwnerNotRegistered")));
        var unrelated = await store.RequestAsync(new(CollectionResourceType.Owner, "JRA", "owner-" + Guid.NewGuid()), definition, 1,
            CollectionReason.Discovery, now, attributes: new Dictionary<string, string> { ["name"] = name, ["requestedByRaceId"] = saved.Result.RaceId });
        var unrelatedLease = await store.AcquireAsync(unrelated.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(unrelatedLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(unrelatedLease.TaskId, unrelatedLease.LeaseToken, DateTimeOffset.UtcNow,
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "OwnerNotRegistered")));
        var candidates = await http.GetFromJsonAsync<PreviewOwnerIdentityRecoveryResponse>("/api/admin/repairs/owner-identity");
        var candidate = candidates!.Candidates.Single(x => x.TaskId == original.TaskId);
        var blocked = candidates.Candidates.Single(x => x.TaskId == unrelated.TaskId);
        Assert.AreEqual("NotKnownFaultyOwnerId", blocked.BlockingReason);
        Assert.IsNull(candidate.BlockingReason);
        Assert.AreEqual(target, candidate.TargetId);
        var request = new ExecuteOwnerIdentityRecoveryRequest([new(candidate.NotificationId, candidate.Fingerprint!)]);
        const string path = "/api/admin/repairs/owner-identity/execute";
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path, request)).StatusCode);
        await store.SetPausedAsync(true, "test recovery", now);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path,
            new ExecuteOwnerIdentityRecoveryRequest([request.Items[0], new(blocked.NotificationId, "unrelated")]))).StatusCode);
        Assert.IsEmpty(await store.GetBatchResourceStatusesAsync($"owner-identity-contract-v1:{candidate.NotificationId:N}"));
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path,
            new ExecuteOwnerIdentityRecoveryRequest([new(candidate.NotificationId, "stale")]))).StatusCode);
        var applied = await http.PostAsJsonAsync(path, request);
        applied.EnsureSuccessStatusCode();
        var first = (await applied.Content.ReadFromJsonAsync<ExecuteOwnerIdentityRecoveryResponse>())!.Receipts.Single();
        var replay = await http.PostAsJsonAsync(path, request);
        replay.EnsureSuccessStatusCode();
        Assert.AreEqual(first.RequestId, (await replay.Content.ReadFromJsonAsync<ExecuteOwnerIdentityRecoveryResponse>())!.Receipts.Single().RequestId);
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
        (await http.PostAsJsonAsync("/api/races", new CreateRaceRequest(new(date, "TOKYO", 1, "旧ID", id)))).EnsureSuccessStatusCode();
        foreach (var course in new[] { "東京", "Tokyo", "tokyo" })
        {
            var resolved = await http.PostAsJsonAsync("/api/identity/race", new ResolveRaceIdentityRequest(new(date, course, 1)));
            resolved.EnsureSuccessStatusCode();
            Assert.AreEqual(id, (await resolved.Content.ReadFromJsonAsync<ResolveRaceIdentityResponse>())!.Identity.Id);
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
            (await http.PostAsJsonAsync("/api/races", new CreateRaceRequest(new(date, course, 2, "duplicate", "race-" + Guid.NewGuid())))).EnsureSuccessStatusCode();
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/identity/race", new ResolveRaceIdentityRequest(new(date, "Tokyo", 2)))).StatusCode);
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
        (await http.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse(raw, raw, "M", new(2024, 1, 1), legacy))).EnsureSuccessStatusCode();
        Assert.AreEqual(DeterministicIdGenerator.BuildHorseId(raw), DeterministicIdGenerator.BuildHorseId("ABC"));
        var resolved = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest(new("ABC", BirthDate: new(2024, 1, 1))));
        resolved.EnsureSuccessStatusCode();
        Assert.AreEqual(legacy, (await resolved.Content.ReadFromJsonAsync<ResolveHorseIdentityResponse>())!.Identity.Id);
        (await http.PutAsJsonAsync($"/api/horses/{legacy}", new UpdateHorseProfileRequest { HorseId = legacy, Horse = new("ABC", "ABC", "M", new(2024, 1, 1)) })).EnsureSuccessStatusCode();
        var afterNameCorrection = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest(new("ABC")));
        afterNameCorrection.EnsureSuccessStatusCode();
        Assert.AreEqual(legacy, (await afterNameCorrection.Content.ReadFromJsonAsync<ResolveHorseIdentityResponse>())!.Identity.Id);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest(new("ABC", BirthDate: new(2023, 1, 1))))).StatusCode);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest(new("ABC", "https://www.jra.go.jp/JRADB/accessU.html?CNAME=other")))).StatusCode);
        (await http.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse("ABC", "ABC", "M", new(2020, 1, 1), DeterministicIdGenerator.BuildHorseId("ABC")))).EnsureSuccessStatusCode();
        var ambiguous = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest(new("ABC")));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, ambiguous.StatusCode);
        Assert.AreEqual("AmbiguousHorseIdentity", (await ambiguous.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["code"]);
        var collisionId = DeterministicIdGenerator.BuildHorseId("ABC-DEF");
        (await http.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse("ABC-DEF", "ABC-DEF", null, null, collisionId))).EnsureSuccessStatusCode();
        Assert.AreEqual(collisionId, DeterministicIdGenerator.BuildHorseId("ABCDEF"));
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest(new("ABCDEF")))).StatusCode);
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
        (await http.PostAsJsonAsync("/api/horses", SubjectRequestFactory.RegisterHorse("サンプル", "サンプル", "M", new(2024, 1, 1), id))).EnsureSuccessStatusCode();
        var path = $"/api/v2/admin/subjects/Horse/{id}/profile";
        var profile = new JraSubjectProfileDto("Horse", "マル外 サンプル", source, source,
            new() { ["生年月日"] = "2024年1月1日" }, DateTimeOffset.UtcNow);
        (await http.PutAsJsonAsync(path, new PutSubjectProfileRequest("Horse", id, profile))).EnsureSuccessStatusCode();
        (await http.PutAsJsonAsync(path, new PutSubjectProfileRequest("Horse", id, profile with { SourceIdentity = source + "&extra=1" }))).EnsureSuccessStatusCode();
        var resolved = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest(new("サンプル", source)));
        resolved.EnsureSuccessStatusCode();
        Assert.AreEqual(id, (await resolved.Content.ReadFromJsonAsync<ResolveHorseIdentityResponse>())!.Identity.Id);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest(new("サンプル")))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PutAsJsonAsync(path, new PutSubjectProfileRequest("Horse", id, profile with { SourceIdentity = source.Replace("123456", "654321"), SourceUrl = source.Replace("123456", "654321") }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync(path, new PutSubjectProfileRequest("Horse", id, profile with { SourceUrl = source.Replace("www.jra.go.jp", "example.com") }))).StatusCode);
    }
}
