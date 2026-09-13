using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;

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

        var preview = await http.GetFromJsonAsync<HorseIdentityRepairPreviewResponse>(
            "/api/admin/repairs/20260913-jra-horse-identity");
        var candidate = preview!.Candidates.Single();
        Assert.IsTrue(candidate.SafeToApply, candidate.BlockingReason);
        Assert.AreEqual(sourceId, candidate.SourceHorseId);
        Assert.AreEqual(targetId, candidate.TargetHorseId);

        var apply = await http.PostAsJsonAsync("/api/admin/repairs/20260913-jra-horse-identity/apply",
            new ApplyHorseIdentityRepairRequest([candidate.CandidateId]));
        Assert.AreEqual(HttpStatusCode.OK, apply.StatusCode);
        var oldProfile = await http.GetAsync($"/api/horses/{sourceId}");
        Assert.AreEqual(HttpStatusCode.OK, oldProfile.StatusCode);
        var resolved = await oldProfile.Content.ReadFromJsonAsync<HorseRacingPrediction.Contracts.HorseReadModel>();
        Assert.AreEqual(targetId, resolved!.HorseId);

        var second = await http.PostAsJsonAsync("/api/admin/repairs/20260913-jra-horse-identity/apply",
            new ApplyHorseIdentityRepairRequest([candidate.CandidateId]));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var repeated = await second.Content.ReadFromJsonAsync<ApplyHorseIdentityRepairResponse>();
        Assert.AreEqual(0, repeated!.AppliedCount);
        Assert.AreEqual(1, repeated.SkippedCount);
    }
}
