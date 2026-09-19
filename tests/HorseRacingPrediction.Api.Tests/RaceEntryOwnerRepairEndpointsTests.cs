using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class RaceEntryOwnerRepairEndpointsTests
{
    [TestMethod]
    public async Task MigrationPreviewIsReadOnly_AndApplyIsRestartSafe()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", ResourceType.Race,
            2, "owner repair", false);
        var date = new DateOnly(2032, 9, 19);
        var create = new DeclareRaceResultBulkRequest(date, "阪神", 11, "馬主補完検証",
            EntryCount: 1, Entries:
            [new(1, 1, "1:24.0", null, null, null, null, HorseName: "補完対象馬")],
            WinningHorseName: "補完対象馬", DeclaredAt: DateTimeOffset.UtcNow);
        var createdResponse = await http.PostAsJsonAsync("/api/races/result-bulk", create);
        var created = await createdResponse.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(created);
        Assert.IsEmpty(created.Errors, string.Join("; ", created.Errors));

        var oldDate = new DateOnly(2020, 1, 5);
        var oldCreate = create with { RaceDate = oldDate, RaceNumber = 9, RaceName = "期間外検証" };
        var oldResponse = await http.PostAsJsonAsync("/api/races/result-bulk", oldCreate);
        var oldCreated = await oldResponse.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(oldCreated);
        Assert.IsEmpty(oldCreated.Errors, string.Join("; ", oldCreated.Errors));

        var preview = await http.GetFromJsonAsync<RaceEntryOwnerRepairPreview>(
            $"/api/admin/collection/repairs/race-entry-owners/preview?date={date:yyyy-MM-dd}");
        var repeated = await http.GetFromJsonAsync<RaceEntryOwnerRepairPreview>(
            $"/api/admin/collection/repairs/race-entry-owners/preview?date={date:yyyy-MM-dd}");

        Assert.IsNotNull(preview);
        Assert.HasCount(1, preview.Candidates);
        Assert.AreEqual(1, preview.Candidates[0].MissingOwnerCount);
        CollectionAssert.AreEqual(preview.Candidates.Select(x => x.RaceId).ToArray(),
            repeated!.Candidates.Select(x => x.RaceId).ToArray());

        using var migrationPreviewResponse = await http.PostAsync(
            "/api/admin/collection/migrations/race-entry-owners/preview", null);
        var migrationPreview = await migrationPreviewResponse.Content
            .ReadFromJsonAsync<RaceEntryOwnerMigrationProgress>();
        Assert.IsNotNull(migrationPreview);
        Assert.IsTrue(migrationPreview.Candidates.Any(x => x.RaceId == created.RaceId));
        var outside = migrationPreview.Candidates.Single(x => x.RaceId == oldCreated.RaceId);
        Assert.AreEqual(RaceEntryOwnerRepairEligibility.OutsideCardLookupPeriod, outside.Eligibility);
        Assert.AreEqual(1, migrationPreview.Eligible);
        Assert.AreEqual(1, migrationPreview.OutsideCardLookupPeriod);
        Assert.AreEqual(0, migrationPreview.Requested);

        await store.SetPausedAsync(true, "test migration", DateTimeOffset.UtcNow);
        using var apply = await http.PostAsync(
            "/api/admin/collection/migrations/race-entry-owners/apply", null);
        using var repeatedApply = await http.PostAsync(
            "/api/admin/collection/migrations/race-entry-owners/apply", null);
        var progress = await repeatedApply.Content.ReadFromJsonAsync<RaceEntryOwnerMigrationProgress>();

        Assert.AreEqual(HttpStatusCode.OK, apply.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, repeatedApply.StatusCode);
        Assert.IsNotNull(progress);
        Assert.AreEqual(1, progress.Requested);
        Assert.AreEqual(1, progress.Processing);
        Assert.AreEqual(0, progress.Eligible);
        Assert.AreEqual(RaceEntryOwnerRepairEligibility.ExistingRequest,
            progress.Candidates.Single(x => x.RaceId == created.RaceId).Eligibility);
        Assert.HasCount(1, (await store.GetTasksAsync()).Where(x => x.Resource.Id == preview.Candidates[0].ResourceId));
        Assert.IsFalse((await store.GetTasksAsync()).Any(x => x.Resource.Id == outside.ResourceId));
    }

    [TestMethod]
    public async Task LaterRaceCard_EnrichesOwner_AndReplayDoesNotCreateMoreEvents()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var date = new DateOnly(2033, 9, 19);
        var initial = new DeclareRaceResultBulkRequest(date, "阪神", 10, "後着補完検証",
            EntryCount: 1, Entries:
            [new(1, 1, "1:24.0", null, null, null, null, HorseName: "後着対象馬")],
            WinningHorseName: "後着対象馬", DeclaredAt: DateTimeOffset.UtcNow);
        var initialResponse = await http.PostAsJsonAsync("/api/races/result-bulk", initial);
        var created = await initialResponse.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(created);
        Assert.IsEmpty(created.Errors, string.Join("; ", created.Errors));
        var card = initial with
        {
            TargetRaceId = created.RaceId,
            RefreshExistingData = true,
            IsRaceCard = true,
            Entries = [new(1, null, null, null, null, null, null,
                HorseName: "後着対象馬", OwnerName: "補完 馬主")],
            WinningHorseName = null,
            DeclaredAt = null
        };

        using var first = await http.PostAsJsonAsync("/api/races/result-bulk", card);
        using var replay = await http.PostAsJsonAsync("/api/races/result-bulk", card);
        var race = await http.GetFromJsonAsync<HorseRacingPrediction.Api.Contracts.RaceResponse>(
            $"/api/races/{created.RaceId}");

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.IsNotNull(race);
        Assert.AreEqual("補完 馬主", race.Entries.Single().OwnerName);
    }
}
