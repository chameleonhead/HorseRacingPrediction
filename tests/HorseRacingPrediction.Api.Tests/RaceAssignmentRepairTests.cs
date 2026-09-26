using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RacePredictionContextReadModel = HorseRacingPrediction.Application.Queries.ReadModels.RacePredictionContextReadModel;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class RaceAssignmentRepairTests
{
    private const string HorseA = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002023100001/AA";
    private const string HorseB = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002023100002/BB";

    [TestMethod]
    [DataRow(14)]
    [DataRow(16)]
    public async Task FullCardCycleAndSharedJockey_PreserveHorseAttributesAndHistories(int count)
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var raceId = await SeedAsync(app, http, 6);
        string Source(int n) => $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud00202410{n:D4}/AB";
        var initial = new DeclareRaceResultBulkRequest(new(2026, 9, 26), "中山", 5, "循環入替検証", EntryCount: count, IsRaceCard: true,
            Entries: Enumerable.Range(1, count).Select(n => new RaceResultEntryBulkDto(n, null, null, null, null, null, null,
                HorseName: $"循環馬{n}", HorseSourceIdentity: Source(n), JockeyName: "同一騎手", AssignedWeight: 54 + n % 3, BodyWeight: 450 + n)).ToArray());
        var created = await (await http.PostAsJsonAsync("/api/races/result-bulk", initial)).Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(created);
        Assert.IsEmpty(created.Errors, string.Join(";", created.Errors));
        raceId = created.RaceId;
        var inspection = await http.GetFromJsonAsync<JsonElement>($"/api/admin/races/{raceId}/entry-repair");
        var manifest = Manifest(inspection.GetProperty("version").GetInt32()) with
        { GradeCode = "G3", Horses = Enumerable.Range(1, count).Select(n => new RaceEntryRepairHorse(Source(n), n % count + 1, (n % count) / 2 + 1, $"所有者{n}")).ToArray() };
        var preview = await (await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/preview", manifest)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(preview.GetProperty("eligible").GetBoolean(), preview.ToString());
        var request = new ApplyRaceEntryRepairRequest(Guid.NewGuid().ToString(), preview.GetProperty("fingerprint").GetString()!, manifest);
        var applied = await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/apply", request);
        Assert.AreEqual(HttpStatusCode.OK, applied.StatusCode, await applied.Content.ReadAsStringAsync());
        var after = await http.GetFromJsonAsync<JsonElement>($"/api/admin/races/{raceId}/entry-repair");
        Assert.AreEqual(0, after.GetProperty("blockers").GetArrayLength(), after.ToString());
        var entries = after.GetProperty("race").GetProperty("entries").EnumerateArray().ToArray();
        for (var n = 1; n <= count; n++)
        {
            var entry = entries.Single(x => x.GetProperty("horseId").GetString() == DeterministicIdGenerator.BuildHorseId("", Source(n)));
            Assert.AreEqual(n % count + 1, entry.GetProperty("horseNumber").GetInt32());
            Assert.AreEqual(450m + n, entry.GetProperty("declaredWeight").GetDecimal());
            Assert.AreEqual($"所有者{n}", entry.GetProperty("ownerName").GetString());
        }
    }

    [TestMethod]
    public async Task ManifestMismatchAndNewReferenceAfterPreview_AreRejectedWithoutRepairEvent()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var raceId = await SeedAsync(app, http);
        var inspection = await http.GetFromJsonAsync<JsonElement>($"/api/admin/races/{raceId}/entry-repair");
        var manifest = Manifest(inspection.GetProperty("version").GetInt32());
        foreach (var invalid in new[] { manifest with { ExpectedVersion = 0 }, manifest with { Horses = [manifest.Horses[0]] },
            manifest with { Horses = [manifest.Horses[0], manifest.Horses[0]] },
            manifest with { SourceUrl = manifest.SourceUrl.Replace("0106", "0109") } })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/preview", invalid)).StatusCode);
        var preview = await (await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/preview", manifest)).Content.ReadFromJsonAsync<JsonElement>();
        var ticketId = "predictionticket-" + Guid.NewGuid();
        (await http.PostAsJsonAsync("/api/predictions", new HorseRacingPrediction.Api.Contracts.CreatePredictionTicketRequest(
            raceId, "AI", "local", 0.5m, null, ticketId))).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync($"/api/predictions/{ticketId}/withdraw", new { reason = "withdrawn" })).EnsureSuccessStatusCode();
        var applied = await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/apply",
            new ApplyRaceEntryRepairRequest(Guid.NewGuid().ToString(), preview.GetProperty("fingerprint").GetString()!, manifest));
        Assert.AreEqual(HttpStatusCode.Conflict, applied.StatusCode);
        using var db = app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext();
        Assert.AreEqual(0, await db.Set<EventEntity>().CountAsync(x => x.AggregateId == raceId && x.Data.Contains("SourceEvidenceJson")));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE FutureIndependentReferences (RaceId TEXT)");
        var unknown = await http.GetFromJsonAsync<JsonElement>($"/api/admin/races/{raceId}/entry-repair");
        StringAssert.Contains(unknown.GetProperty("blockers").ToString(), "UnknownSchemaTable:FutureIndependentReferences");
    }

    [TestMethod]
    public async Task RepairEndpointsRequireApiKey_AndDeletedHorseMemoBlocksRepair()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var raceId = await SeedAsync(app, http);
        http.DefaultRequestHeaders.Remove("X-Api-Key");
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await http.GetAsync($"/api/admin/races/{raceId}/entry-repair")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/preview", Manifest(1))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/apply",
            new ApplyRaceEntryRepairRequest(Guid.NewGuid().ToString(), "invalid", Manifest(1)))).StatusCode);
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        Assert.ThrowsExactly<ArgumentException>(() => new HorseRacingPrediction.Domain.Memos.MemoId("notes-1"));
        var memoId = "memo-" + Guid.NewGuid();
        var memo = new HorseRacingPrediction.Api.Contracts.CreateMemoRequest("human", "Note", "1番を注目", DateTimeOffset.UtcNow,
            [new("Horse", DeterministicIdGenerator.BuildHorseId("", HorseA))], MemoId: memoId);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/memos", memo)).StatusCode);
        (await http.DeleteAsync("/api/memos/" + memoId)).EnsureSuccessStatusCode();
        var inspection = await http.GetFromJsonAsync<JsonElement>($"/api/admin/races/{raceId}/entry-repair");
        StringAssert.Contains(inspection.GetProperty("blockers").ToString(), "MemoReference:" + memoId);
    }

    [TestMethod]
    public async Task ProjectionFailure_BlocksReadsAndWrites_AndRecoversAfterProcessRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hrp-repair-restart", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connection = "Data Source=" + Path.Combine(directory, "test.db");
        var failure = new ProjectionFailure();
        var (app, http) = await TestApplicationFactory.CreateAsync(connection, [failure]);
        var raceId = await SeedAsync(app, http);
        var otherRaceId = await SeedAsync(app, http, 6);
        var oldBackup = Path.Combine(directory, "before-repair.db");
        Backup(connection, oldBackup);
        var inspection = await http.GetFromJsonAsync<JsonElement>($"/api/admin/races/{raceId}/entry-repair");
        var manifest = Manifest(inspection.GetProperty("version").GetInt32());
        var previewResponse = await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/preview", manifest);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var request = new ApplyRaceEntryRepairRequest(Guid.NewGuid().ToString(), preview.GetProperty("fingerprint").GetString()!, manifest);
        failure.Enabled = true;
        try { await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/apply", request); }
        catch (Exception) { /* TestServer propagates the deliberately injected database failure. */ }
        Assert.IsFalse((await app.Services.GetRequiredService<RaceWriteCoordinator>().ReadBarrierAsync(raceId))!.Verified);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.GetAsync($"/api/races/{raceId}/context")).StatusCode);
        var horseId = DeterministicIdGenerator.BuildHorseId("", HorseA);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.GetAsync($"/api/horses/{horseId}/race-history")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.GetAsync($"/api/horses/{horseId}/weight-history")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.GetAsync($"/api/races/{otherRaceId}/ml-prediction")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/races/{raceId}/weather",
            new { observedAt = DateTimeOffset.UtcNow, conditionCode = "SUNNY" })).StatusCode);
        failure.Enabled = false;
        http.Dispose();
        await app.DisposeAsync();
        var (restarted, client) = await TestApplicationFactory.CreateAsync(connection);
        await using var restartedApp = restarted;
        using var resumed = client;
        resumed.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var response = await resumed.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/apply", request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.OK, (await resumed.GetAsync($"/api/races/{raceId}/context")).StatusCode);
        using var db = restarted.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext();
        Assert.AreEqual(1, await db.Set<EventEntity>().CountAsync(x => x.AggregateId == raceId && x.Data.Contains(request.OperationId)));
        var afterBackup = Path.Combine(directory, "after-repair.db");
        Backup(connection, afterBackup);
        var (restored, restoredHttp) = await TestApplicationFactory.CreateAsync("Data Source=" + afterBackup);
        await using var restoredApp = restored;
        using var restoredClient = restoredHttp;
        restoredClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        Assert.AreEqual(HttpStatusCode.Conflict, (await restoredClient.GetAsync($"/api/races/{raceId}/context")).StatusCode);
        var recovered = await restoredClient.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/apply", request);
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync());
        var (oldRestored, oldHttp) = await TestApplicationFactory.CreateAsync("Data Source=" + oldBackup);
        await using var oldApp = oldRestored;
        using var oldClient = oldHttp;
        oldClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        // Restore the pre-repair DB with a newer verified sidecar: disagreement must stay closed.
        oldApp.Services.GetRequiredService<RaceWriteCoordinator>().WriteBarrier(raceId, new(request.OperationId, request.Fingerprint, true));
        Assert.AreEqual(HttpStatusCode.Conflict, (await oldClient.GetAsync($"/api/races/{raceId}/context")).StatusCode);
    }

    private static void Backup(string connection, string destination)
    {
        using var source = new Microsoft.Data.Sqlite.SqliteConnection(connection);
        using var target = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + destination);
        source.Open(); target.Open(); source.BackupDatabase(target);
    }

    private sealed class ProjectionFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Enabled && data.Context!.ChangeTracker.Entries<RacePredictionContextReadModel>()
                .Any(x => x.State is EntityState.Modified or EntityState.Added))
                throw new InvalidOperationException("Injected repair projection failure");
            return base.SavingChangesAsync(data, result, token);
        }
    }

    [TestMethod]
    public async Task PreviewIsReadOnly_RepairSwapsWholeHorseAssignments_AndRetryAddsNoEvent()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var raceId = await SeedAsync(app, http);
        var provider = app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>();
        using var db = provider.CreateContext();
        var before = await db.Set<EventEntity>().CountAsync();
        var predictor = new HorseRacingPrediction.Predictor.Scheduling.ApiOnlyPredictionWorkflow(
            new HorseRacingPrediction.Collector.Http.HttpRaceQueryService(http), new HorseRacingPrediction.Collector.Http.HttpPredictionWriteService(http),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HorseRacingPrediction.Predictor.Scheduling.ApiOnlyPredictionWorkflow>.Instance);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => predictor.RunAsync(raceId));
        Assert.AreEqual(before, await db.Set<EventEntity>().CountAsync());
        var inspection = await http.GetFromJsonAsync<JsonElement>($"/api/admin/races/{raceId}/entry-repair");
        Assert.AreEqual(0, inspection.GetProperty("blockers").GetArrayLength(), inspection.ToString());
        var manifest = Manifest(inspection.GetProperty("version").GetInt32());
        var previewResponse = await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/preview", manifest);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode, preview.ToString());
        Assert.IsTrue(preview.GetProperty("eligible").GetBoolean(), preview.ToString());
        Assert.AreEqual(before, await db.Set<EventEntity>().CountAsync());
        var request = new ApplyRaceEntryRepairRequest(Guid.NewGuid().ToString(), preview.GetProperty("fingerprint").GetString()!, manifest);
        var applied = await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/apply", request);
        Assert.AreEqual(HttpStatusCode.OK, applied.StatusCode, await applied.Content.ReadAsStringAsync());
        var repeated = await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/apply", request);
        Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode, await repeated.Content.ReadAsStringAsync());
        Assert.AreEqual(before + 1, await db.Set<EventEntity>().CountAsync());
        var context = await http.GetFromJsonAsync<JsonElement>($"/api/races/{raceId}/context");
        var entries = context.GetProperty("entries").EnumerateArray().ToArray();
        Assert.AreEqual(DeterministicIdGenerator.BuildHorseId("", HorseB), entries.Single(x => x.GetProperty("horseNumber").GetInt32() == 1).GetProperty("horseId").GetString());
        Assert.AreEqual("馬主B", entries.Single(x => x.GetProperty("horseNumber").GetInt32() == 1).GetProperty("ownerName").GetString());
        Assert.AreEqual(request.Fingerprint, context.GetProperty("entryAssignmentFingerprint").GetString());
        var stalePrediction = await http.PostAsJsonAsync("/api/predictions", new
        { raceId, predictorType = "Human", predictorId = "local-test", confidenceScore = 0.5m, summaryComment = "old context" });
        Assert.AreEqual(HttpStatusCode.Conflict, stalePrediction.StatusCode);
        var predicted = await predictor.RunAsync(raceId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(predicted.PredictionTicketId));
    }

    [TestMethod]
    public async Task IndependentOddsReferenceBlocksPreview()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var raceId = await SeedAsync(app, http);
        var odds = await http.PostAsJsonAsync($"/api/admin/races/{raceId}/odds-snapshots", new
        { observedAt = DateTimeOffset.UtcNow, entries = new[] { new { horseNumber = 1, winOdds = 2.5m, popularity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Accepted, odds.StatusCode, await odds.Content.ReadAsStringAsync());
        var inspection = await http.GetFromJsonAsync<JsonElement>($"/api/admin/races/{raceId}/entry-repair");
        var response = await http.PostAsJsonAsync($"/api/admin/races/{raceId}/entry-repair/preview", Manifest(inspection.GetProperty("version").GetInt32()));
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(preview.GetProperty("eligible").GetBoolean());
        StringAssert.Contains(preview.ToString(), "RaceOddsSnapshotRecorded");
    }

    private static RaceEntryRepairManifest Manifest(int version) => new(version,
        "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604080520260926/AC",
        DateTimeOffset.UtcNow, null, [new(HorseA, 2, 2, "馬主A"), new(HorseB, 1, 1, "馬主B")], new string('A', 64));

    private static async Task<string> SeedAsync(WebApplication app, HttpClient http, int raceNumber = 5)
    {
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse, 3, "repair test", true);
        await store.RegisterDefinitionAsync(new("jockey-profile"), "Jockey profile", ResourceType.Jockey, 3, "repair test", true);
        if (!http.DefaultRequestHeaders.Contains("X-Api-Key")) http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var request = new DeclareRaceResultBulkRequest(new DateOnly(2026, 9, 26), "中山", raceNumber, "ローカル補正検証",
            EntryCount: 2, IsRaceCard: true, Entries:
            [new(1, null, null, null, null, null, null, HorseName: "テストA", HorseSourceIdentity: HorseA, JockeyName: "騎手A", BodyWeight: 470),
             new(2, null, null, null, null, null, null, HorseName: "テストB", HorseSourceIdentity: HorseB, JockeyName: "騎手B", BodyWeight: 480)]);
        var response = await http.PostAsJsonAsync("/api/races/result-bulk", request);
        var result = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(result, await response.Content.ReadAsStringAsync());
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        return result.RaceId;
    }
}
