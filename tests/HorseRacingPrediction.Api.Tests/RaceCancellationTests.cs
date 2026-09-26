using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Predictor.Scheduling;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared = HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class RaceCancellationTests
{
    private static async Task<(WebApplication, HttpClient)> CreateAsync()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", ResourceType.Horse, 4, "test", true);
        await store.RegisterDefinitionAsync(new("owner-identity"), "Owner", ResourceType.Owner, 1, "test", true);
        return (app, http);
    }
    private static DeclareRaceResultBulkRequest Card(bool initiallyCancelled = true) => new(
        new(2026, 9, 27), "中山", 1, "取消検証", EntryCount: 16, GradeCode: "Maiden", SurfaceCode: "Dirt",
        DistanceMeters: 1200, DirectionCode: "Right", IsRaceCard: true,
        Entries: Enumerable.Range(1, 16).Select(n => new RaceResultEntryBulkDto(
            initiallyCancelled && n == 6 ? null : n, null, null, null, null, null, null,
            HorseName: $"馬{n}", OwnerName: $"馬主{n}", GateNumber: (n + 1) / 2, AssignedWeight: 55,
            HorseSourceIdentity: $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud00202410{n:D4}/AB",
            ParticipationStatus: initiallyCancelled && n == 6 ? RaceEntryParticipationStatus.Cancelled : RaceEntryParticipationStatus.Active)).ToArray());

    private static async Task<string> SaveAsync(HttpClient http, DeclareRaceResultBulkRequest card)
    {
        var response = await http.PostAsJsonAsync("/api/races/result-bulk", card);
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(saved);
        Assert.IsEmpty(saved.Errors, string.Join(";", saved.Errors));
        Assert.IsTrue(saved.CorePersisted);
        return saved.RaceId;
    }

    [TestMethod]
    public async Task CancelledCard_RoundTripsAll16Entries_AndPredictsOnly15()
    {
        var (app, client) = await CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = await SaveAsync(http, Card());
        var query = new HttpRaceQueryService(http);
        var context = await query.GetRacePredictionContextAsync(raceId);
        Assert.IsNotNull(context);
        Assert.HasCount(16, context.Entries);
        var cancelled = context.Entries.Single(x => x.ParticipationStatus == RaceEntryParticipationStatus.Cancelled);
        Assert.IsNull(cancelled.HorseNumber);
        Assert.AreEqual("馬主6", cancelled.OwnerName);
        var cardJson = await http.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/races/{raceId}");
        Assert.AreEqual(16, cardJson.GetProperty("entries").GetArrayLength());
        var ml = await query.GetMlPredictionAsync(raceId);
        Assert.IsNotNull(ml);
        Assert.HasCount(15, ml.Rankings);
        Assert.IsFalse(ml.Rankings.Any(x => x.EntryId == cancelled.EntryId));
        var workflow = new ApiOnlyPredictionWorkflow(query, new HttpPredictionWriteService(http), NullLogger<ApiOnlyPredictionWorkflow>.Instance);
        var prediction = await workflow.RunAsync(raceId);
        Assert.IsFalse(prediction.Skipped);
        var ticket = await http.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/predictions/{prediction.PredictionTicketId}");
        Assert.AreEqual(15, ticket.GetProperty("marks").GetArrayLength());
        var after = await query.GetRacePredictionContextAsync(raceId);
        Assert.HasCount(16, after!.Entries);
        Assert.AreEqual(cancelled, after.Entries.Single(x => x.EntryId == cancelled.EntryId));
    }

    [TestMethod]
    public async Task Cancellation_InvalidatesReceiptAndDraftMarks_OmittedStatusPreservesCancellation()
    {
        var (app, client) = await CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var initial = Card(false);
        var raceId = await SaveAsync(http, initial);
        var query = new HttpRaceQueryService(http);
        var before = (await query.GetRacePredictionContextAsync(raceId))!;
        var horse = before.Entries.Single(x => x.HorseNumber == 6);
        var writer = new HttpPredictionWriteService(http);
        var ticket = await writer.CreateBoundPredictionTicketAsync(raceId, "test", "test", .5m, "test", before.EntryAssignmentFingerprint);
        await writer.AddPredictionMarkAsync(ticket, horse.EntryId, "◎", 1, 90, "test");
        var historicalTicket = await writer.CreateBoundPredictionTicketAsync(raceId, "test", "history", .5m, "test", before.EntryAssignmentFingerprint);
        await writer.AddPredictionMarkAsync(historicalTicket, horse.EntryId, "◎", 1, 90, "test");
        await writer.FinalizePredictionTicketAsync(historicalTicket);
        var cancelledCard = initial with
        {
            TargetRaceId = raceId,
            RefreshExistingData = true,
            Entries = initial.Entries!.Select(x => x.HorseNumber == 6
            ? x with { HorseNumber = null, ParticipationStatus = RaceEntryParticipationStatus.Cancelled } : x).ToArray()
        };
        await SaveAsync(http, cancelledCard);
        var after = (await query.GetRacePredictionContextAsync(raceId))!;
        var cancelled = after.Entries.Single(x => x.EntryId == horse.EntryId);
        Assert.AreEqual(6, cancelled.HorseNumber);
        Assert.AreEqual(horse.HorseId, cancelled.HorseId);
        Assert.AreNotEqual(before.EntryAssignmentFingerprint, after.EntryAssignmentFingerprint);
        http.DefaultRequestHeaders.Add("X-Race-Hold-Generation", "0");
        http.DefaultRequestHeaders.Add("X-Race-Assignment-Fingerprint", before.EntryAssignmentFingerprint);
        var odds = new HorseRacingPrediction.ApiClient.RecordRaceOddsSnapshotRequest(DateTimeOffset.UtcNow, [new(1, 2.5m)]);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/admin/races/{raceId}/odds-snapshots", odds)).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Race-Assignment-Fingerprint");
        http.DefaultRequestHeaders.Add("X-Race-Assignment-Fingerprint", after.EntryAssignmentFingerprint);
        Assert.AreEqual(HttpStatusCode.Accepted, (await http.PostAsJsonAsync($"/api/admin/races/{raceId}/odds-snapshots", odds)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"/api/admin/races/{raceId}/odds-snapshots", odds with { Entries = [new(6, 3m)] })).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Race-Assignment-Fingerprint");
        http.DefaultRequestHeaders.Remove("X-Race-Hold-Generation");
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/predictions/{ticket}/marks", new AddPredictionMarkRequest(horse.EntryId, "○", 2, 80, null))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/predictions/{ticket}/marks", new AddPredictionMarkRequest(before.Entries.First(x => x.HorseNumber == 1).EntryId, "▲", 3, 70, null))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsync($"/api/predictions/{ticket}/finalize", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/predictions", new CreatePredictionTicketRequest(raceId, "test", "test", .5m, null, EntryAssignmentFingerprint: before.EntryAssignmentFingerprint))).StatusCode);
        await SaveAsync(http, initial with { Entries = initial.Entries!.Select(x => x with { ParticipationStatus = null }).ToArray() });
        var preserved = (await query.GetRacePredictionContextAsync(raceId))!.Entries.Single(x => x.EntryId == horse.EntryId);
        Assert.AreEqual(RaceEntryParticipationStatus.Cancelled, preserved.ParticipationStatus);
        Assert.AreEqual("馬主6", preserved.OwnerName);
        var history = await http.GetFromJsonAsync<PredictionTicketResponse>($"/api/predictions/{historicalTicket}");
        Assert.AreEqual(TicketStatus.Finalized, history!.TicketStatus);
        Assert.HasCount(1, history.Marks);
        Assert.AreEqual(horse.EntryId, history.Marks.Single().EntryId);
    }

    [TestMethod]
    public async Task AllCancelled_PreservesCardAndSkipsPrediction()
    {
        var (app, client) = await CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var card = Card() with { Entries = Card().Entries!.Select(x => x with { HorseNumber = null, ParticipationStatus = RaceEntryParticipationStatus.Cancelled }).ToArray() };
        var raceId = await SaveAsync(http, card);
        var query = new HttpRaceQueryService(http);
        var result = await new ApiOnlyPredictionWorkflow(query, new HttpPredictionWriteService(http), NullLogger<ApiOnlyPredictionWorkflow>.Instance).RunAsync(raceId);
        Assert.IsTrue(result.Skipped);
        Assert.AreEqual(string.Empty, result.PredictionTicketId);
        Assert.HasCount(16, (await query.GetRacePredictionContextAsync(raceId))!.Entries);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidStatusOrMissingIdentity_IsRejectedBeforePersistence(bool missingIdentity)
    {
        var (app, client) = await CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var card = Card();
        var invalid = card with
        {
            Entries = card.Entries!.Select((x, i) => i == 5
            ? missingIdentity ? x with { HorseNumber = 6, HorseSourceIdentity = null }
                : x with { ParticipationStatus = (RaceEntryParticipationStatus)99 }
            : x).ToArray()
        };
        var response = await http.PostAsJsonAsync("/api/races/result-bulk", invalid);
        response.EnsureSuccessStatusCode();
        var rejected = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(rejected);
        Assert.IsFalse(rejected.CorePersisted);
        Assert.IsNotEmpty(rejected.Errors);
        Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync($"/api/races/{rejected.RaceId}/context")).StatusCode);
    }
}
