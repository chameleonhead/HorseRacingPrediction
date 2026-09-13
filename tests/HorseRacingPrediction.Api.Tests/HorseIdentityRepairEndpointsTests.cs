using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using EventFlow.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityRepairEndpointsTests
{
    [TestMethod]
    public async Task RecordedRaceEntryReplacement_CanBePreviewedAndAppliedIdempotently()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"修復テスト馬{suffix}";
        var sourceId = DeterministicIdGenerator.BuildHorseId(name);
        var sourceUrl = $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud00{suffix}/45";
        var targetId = DeterministicIdGenerator.BuildHorseId(name, sourceUrl);
        var raceId = $"race-{Guid.NewGuid()}";

        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(name, name, null, null, HorseId: sourceId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(name, name, null, null, HorseId: targetId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new DateOnly(2026, 9, 13), "TOKYO", 1, "修復テスト", raceId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await http.PostAsJsonAsync($"/api/races/{raceId}/card/publish",
            new { EntryCount = 1 })).StatusCode);
        const string entryId = "repair-entry-01";
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(sourceId, 1, null, null, 1, 55, "M", 3, null, null,
                EntryId: entryId, HorseName: name))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(targetId, 1, null, null, 1, 55, "M", 3, null, null,
                EntryId: entryId, HorseName: name, HorseSourceIdentity: sourceUrl))).StatusCode);
        using (var repairDb = application.Services
                   .GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext())
        {
            repairDb.HorseIdentityRepairCandidates.Add(new HorseIdentityRepairCandidateReadModel
            {
                CandidateId = $"20260913-jra-horse-identity-repair:{raceId}:duplicate-evidence",
                RepairId = "20260913-jra-horse-identity-repair",
                SourceHorseId = sourceId,
                TargetHorseId = targetId,
                JraIdentity = $"pw01dud00{suffix}/45",
                RaceId = raceId,
                EntryId = entryId,
                DetectedAt = DateTimeOffset.UtcNow,
            });
            await repairDb.SaveChangesAsync();
        }
        var collectionStore = application.Services.GetRequiredService<CollectionPlatformStore>();
        var sourceResource = new ResourceKey(ResourceType.Horse, "JRA", sourceId);
        const int revision = 1;
        await collectionStore.RegisterDefinitionAsync(new("horse-profile"), "Horse profile",
            ResourceType.Horse, revision, "test", false);
        var sourceTask = await collectionStore.RequestAsync(sourceResource, new("horse-profile"), revision,
            CollectionReason.ManualRefresh, DateTimeOffset.UtcNow);

        var preview = await http.GetFromJsonAsync<HorseIdentityRepairPreviewResponse>(
            "/api/admin/repairs/20260913-jra-horse-identity");
        var candidates = preview!.Candidates.ToArray();
        Assert.HasCount(2, candidates);
        Assert.IsTrue(candidates.All(x => x.SafeToApply), string.Join("; ", candidates.Select(x => x.BlockingReason)));
        Assert.IsTrue(candidates.All(x => x.SourceHorseId == sourceId));
        Assert.IsTrue(candidates.All(x => x.TargetHorseId == targetId));

        var apply = await http.PostAsJsonAsync("/api/admin/repairs/20260913-jra-horse-identity/apply",
            new ApplyHorseIdentityRepairRequest(candidates.Select(x => x.CandidateId).ToArray()));
        Assert.AreEqual(HttpStatusCode.OK, apply.StatusCode);
        var applied = await apply.Content.ReadFromJsonAsync<ApplyHorseIdentityRepairResponse>();
        Assert.AreEqual(1, applied!.DisabledCollectionTaskCount);
        Assert.AreEqual(CollectionTaskStatus.Cancelled,
            (await collectionStore.GetTasksAsync()).Single(x => x.TaskId == sourceTask.TaskId).Status);
        await Assert.ThrowsExactlyAsync<CollectionResourceSuppressedException>(() =>
            collectionStore.RequestAsync(sourceResource, new("horse-profile"), revision,
                CollectionReason.ManualRefresh, DateTimeOffset.UtcNow));
        var oldProfile = await http.GetAsync($"/api/horses/{sourceId}");
        Assert.AreEqual(HttpStatusCode.OK, oldProfile.StatusCode);
        var resolved = await oldProfile.Content.ReadFromJsonAsync<HorseRacingPrediction.Contracts.HorseReadModel>();
        Assert.AreEqual(targetId, resolved!.HorseId);

        var second = await http.PostAsJsonAsync("/api/admin/repairs/20260913-jra-horse-identity/apply",
            new ApplyHorseIdentityRepairRequest(candidates.Select(x => x.CandidateId).ToArray()));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var repeated = await second.Content.ReadFromJsonAsync<ApplyHorseIdentityRepairResponse>();
        Assert.AreEqual(0, repeated!.AppliedCount);
        Assert.AreEqual(2, repeated.SkippedCount);
    }
}
