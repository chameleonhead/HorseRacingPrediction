using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class RaceOddsSnapshotApiTests
{
    [TestMethod]
    public async Task TwoObservationsRemainAsTwoAppendOnlySnapshots()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        const string raceId = "race-00000000-0000-0000-0000-000000000011";
        await CreateRaceWithEntriesAsync(client, raceId, 11, (1, "horse-00000000-0000-0000-0000-000000000011"));
        var fence = await GetFenceAsync(client, raceId);
        var firstAt = new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero);
        var secondAt = firstAt.AddMinutes(2);
        foreach (var request in new[]
        {
            new RecordRaceOddsSnapshotRequest(firstAt, [new(1, 2.5m, 1)]),
            new RecordRaceOddsSnapshotRequest(secondAt, [new(1, 2.3m, 1)]),
        })
        {
            using var response = await PostOddsAsync(client, raceId, request, fence);
            response.EnsureSuccessStatusCode();
        }

        var snapshots = await client.GetFromJsonAsync<List<RaceOddsSnapshot>>(
            $"/api/admin/races/{raceId}/odds-snapshots");
        Assert.IsNotNull(snapshots);
        Assert.HasCount(2, snapshots);
        Assert.AreEqual(firstAt, snapshots[0].ObservedAt);
        Assert.AreEqual(2.5m, snapshots[0].Entries[0].WinOdds);
        Assert.AreEqual(secondAt, snapshots[1].ObservedAt);
        Assert.AreEqual(2.3m, snapshots[1].Entries[0].WinOdds);
        Assert.AreEqual("horse-00000000-0000-0000-0000-000000000011", snapshots[0].Assignments![0].HorseId);
        Assert.AreEqual(0L, fence.Generation);
    }

    [TestMethod]
    public async Task SnapshotRetainsMultipleMarketsAndSelections()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        const string raceId = "race-00000000-0000-0000-0000-000000000012";
        await CreateRaceWithEntriesAsync(client, raceId, 12,
            (1, "horse-00000000-0000-0000-0000-000000000021"),
            (3, "horse-00000000-0000-0000-0000-000000000023"),
            (7, "horse-00000000-0000-0000-0000-000000000027"));
        foreach (var (number, frame) in new[] { (1, 1), (3, 1), (7, 2) })
        {
            var horseId = $"horse-00000000-0000-0000-0000-00000000002{number}";
            using var assigned = await client.PostAsJsonAsync($"/api/races/{raceId}/entries",
                new RegisterEntryRequest(horseId, number, null, null, frame, null, null, null, null, null));
            assigned.EnsureSuccessStatusCode();
        }
        var fence = await GetFenceAsync(client, raceId);
        var observedAt = new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero);
        var request = new RecordRaceOddsSnapshotRequest(observedAt, [new(1, 2.5m, 1)],
        [
            new("Win", "1", 2.5m, 1),
            new("Place", "3", 1.5m, 1),
            new("Quinella", "1-3", 8.4m, 2),
            new("Trio", "1-3-7", 14.2m, 4),
            new("BracketQuinella", "1-2", 5.2m, 3),
        ]);
        using var response = await PostOddsAsync(client, raceId, request, fence);
        response.EnsureSuccessStatusCode();

        var snapshots = await client.GetFromJsonAsync<List<RaceOddsSnapshot>>(
            $"/api/admin/races/{raceId}/odds-snapshots");
        Assert.IsNotNull(snapshots);
        Assert.HasCount(1, snapshots);
        Assert.HasCount(5, snapshots[0].Observations!);
        Assert.AreEqual("3", snapshots[0].Observations![1].Selection);
        Assert.AreEqual("1-3", snapshots[0].Observations![2].Selection);
        Assert.AreEqual(8.4m, snapshots[0].Observations![2].Value);
        Assert.AreEqual("1-3-7", snapshots[0].Observations![3].Selection);
        Assert.AreEqual("1-2", snapshots[0].Observations![4].Selection);
        Assert.HasCount(3, snapshots[0].Assignments!);
    }

    [TestMethod]
    [DataRow("{\"observedAt\":\"2026-09-12T05:00:00Z\",\"entries\":null}")]
    [DataRow("{\"observedAt\":\"2026-09-12T05:00:00Z\"}")]
    [DataRow("{\"observedAt\":\"2026-09-12T05:00:00Z\",\"entries\":[],\"observations\":[]}")]
    [DataRow("{\"observedAt\":\"2026-09-12T05:00:00Z\",\"entries\":[null]}")]
    [DataRow("{\"observedAt\":\"2026-09-12T05:00:00Z\",\"entries\":[],\"observations\":[null]}")]
    public async Task EmptyOrMissingOdds_ReturnsValidationProblem(string json)
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = $"race-{Guid.NewGuid()}";
        await CreateRaceWithEntriesAsync(client, raceId, 1, (1, $"horse-{Guid.NewGuid()}"));
        var fence = await GetFenceAsync(client, raceId);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/races/{raceId}/odds-snapshots")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        AddFence(request, fence);
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task InvalidAndDuplicateOdds_ReturnValidationProblem()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = $"race-{Guid.NewGuid()}";
        await CreateRaceWithEntriesAsync(client, raceId, 1, (1, $"horse-{Guid.NewGuid()}"));
        var fence = await GetFenceAsync(client, raceId);
        var requests = new[]
        {
            new RecordRaceOddsSnapshotRequest(DateTimeOffset.UtcNow, [new(1, -1m)]),
            new RecordRaceOddsSnapshotRequest(DateTimeOffset.UtcNow, [new(1, 2m), new(1, 3m)]),
            new RecordRaceOddsSnapshotRequest(DateTimeOffset.UtcNow, [],
                [new("Win", "1", 2m), new(" win ", " 1 ", 3m)]),
            new RecordRaceOddsSnapshotRequest(DateTimeOffset.UtcNow, [], [new("Win", "99", 2m)]),
            new RecordRaceOddsSnapshotRequest(DateTimeOffset.UtcNow, [], [new("Quinella", "1-99", 2m)]),
            new RecordRaceOddsSnapshotRequest(DateTimeOffset.UtcNow, [], [new("BracketQuinella", "1-2", 2m)]),
        };

        foreach (var request in requests)
        {
            using var response = await PostOddsAsync(client, raceId, request, fence);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [TestMethod]
    public async Task EntriesOnlyAndRepeatedEqualValuesRemainValidSnapshots()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        const string raceId = "race-00000000-0000-0000-0000-000000000013";
        await CreateRaceWithEntriesAsync(client, raceId, 10, (1, "horse-00000000-0000-0000-0000-000000000013"));
        var fence = await GetFenceAsync(client, raceId);
        var firstAt = new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero);
        foreach (var observedAt in new[] { firstAt, firstAt.AddMinutes(1) })
        {
            using var response = await PostOddsAsync(client, raceId,
                new RecordRaceOddsSnapshotRequest(observedAt, [new(1, 2.5m, 1)]), fence);
            response.EnsureSuccessStatusCode();
        }

        var snapshots = await client.GetFromJsonAsync<List<RaceOddsSnapshot>>(
            $"/api/admin/races/{raceId}/odds-snapshots");
        Assert.IsNotNull(snapshots);
        Assert.HasCount(2, snapshots);
        Assert.IsTrue(snapshots.All(x => x.Observations is [{ Market: "Win", Selection: "1", Value: 2.5m }]));
    }

    [TestMethod]
    public async Task OldSnapshotKeepsObservedAssignmentsAfterNumberSwap_AndStaleInputIsRejected()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var initialCard = new DeclareRaceResultBulkRequest(new DateOnly(2036, 9, 12), "東京", 2,
            "odds swap", EntryCount: 2, IsRaceCard: true,
            Entries: [BulkEntry(1, "オッズ馬A", "210001"), BulkEntry(2, "オッズ馬B", "210002")]);
        using var initial = await client.PostAsJsonAsync("/api/races/result-bulk", initialCard);
        initial.EnsureSuccessStatusCode();
        var initialBody = await initial.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(initialBody);
        Assert.IsTrue(initialBody.CorePersisted, string.Join(" | ", initialBody.Errors));
        var raceId = initialBody.RaceId;
        var firstHorse = DeterministicIdGenerator.BuildHorseId("オッズ馬A", SourceIdentity("210001"));
        var secondHorse = DeterministicIdGenerator.BuildHorseId("オッズ馬B", SourceIdentity("210002"));
        var beforeFence = await GetFenceAsync(client, raceId);
        var at = new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero);
        using (var first = await PostOddsAsync(client, raceId,
                   new RecordRaceOddsSnapshotRequest(at, [new(1, 2.5m), new(2, 3.5m)]), beforeFence))
            Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);

        using var swapped = await client.PostAsJsonAsync("/api/races/result-bulk", initialCard with
        {
            Entries = [BulkEntry(2, "オッズ馬A", "210001"), BulkEntry(1, "オッズ馬B", "210002")],
        });
        swapped.EnsureSuccessStatusCode();
        var swappedBody = await swapped.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.IsNotNull(swappedBody);
        Assert.IsTrue(swappedBody.CorePersisted, string.Join(" | ", swappedBody.Errors));
        var afterFence = await GetFenceAsync(client, raceId);
        Assert.AreNotEqual(beforeFence.Fingerprint, afterFence.Fingerprint);

        using (var stale = await PostOddsAsync(client, raceId,
                   new RecordRaceOddsSnapshotRequest(at.AddMinutes(1), [new(1, 2.2m)]), beforeFence))
            Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        using (var current = await PostOddsAsync(client, raceId,
                   new RecordRaceOddsSnapshotRequest(at.AddMinutes(2), [new(1, 2.1m)]), afterFence))
            Assert.AreEqual(HttpStatusCode.Accepted, current.StatusCode);

        var snapshots = await client.GetFromJsonAsync<List<RaceOddsSnapshot>>($"/api/admin/races/{raceId}/odds-snapshots");
        Assert.IsNotNull(snapshots);
        Assert.HasCount(2, snapshots);
        Assert.AreEqual(firstHorse, snapshots[0].Assignments!.Single(x => x.HorseNumber == 1).HorseId);
        Assert.AreEqual(secondHorse, snapshots[1].Assignments!.Single(x => x.HorseNumber == 1).HorseId);
        Assert.AreEqual(DeterministicIdGenerator.BuildRaceEntryId(raceId, firstHorse),
            snapshots[0].Assignments!.Single(x => x.HorseNumber == 1).EntryId);
    }

    [TestMethod]
    public async Task UnknownOrUnconfirmedHorseNumbersCannotProduceOddsSnapshots()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = $"race-{Guid.NewGuid()}";
        var firstHorse = $"horse-{Guid.NewGuid()}";
        var secondHorse = $"horse-{Guid.NewGuid()}";
        await CreateRaceWithEntriesAsync(client, raceId, 3, (1, firstHorse), (null, secondHorse));
        var fence = await GetFenceAsync(client, raceId);
        var at = new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero);
        using (var unconfirmed = await PostOddsAsync(client, raceId,
                   new RecordRaceOddsSnapshotRequest(at, [new(1, 2.5m)]), fence))
            Assert.AreNotEqual(HttpStatusCode.Accepted, unconfirmed.StatusCode);

        await RegisterEntryAsync(client, raceId, secondHorse, 2);
        fence = await GetFenceAsync(client, raceId);
        using (var unknown = await PostOddsAsync(client, raceId,
                   new RecordRaceOddsSnapshotRequest(at, [new(9, 2.5m)]), fence))
            Assert.AreNotEqual(HttpStatusCode.Accepted, unknown.StatusCode);
        var snapshots = await client.GetFromJsonAsync<List<RaceOddsSnapshot>>($"/api/admin/races/{raceId}/odds-snapshots");
        Assert.IsNotNull(snapshots);
        Assert.IsEmpty(snapshots);
    }

    private static async Task CreateRaceWithEntriesAsync(HttpClient client, string raceId, int raceNumber,
        params (int? Number, string HorseId)[] entries)
    {
        using var created = await client.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new(2026, 9, 12), "東京", raceNumber, "odds", raceId));
        created.EnsureSuccessStatusCode();
        foreach (var (_, horseId) in entries)
        {
            using var horse = await client.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(horseId, horseId, "M", null, horseId));
            horse.EnsureSuccessStatusCode();
        }
        using var published = await client.PostAsJsonAsync($"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(entries.Length));
        published.EnsureSuccessStatusCode();
        foreach (var (number, horseId) in entries)
            await RegisterEntryAsync(client, raceId, horseId, number);
    }

    private static RaceResultEntryBulkDto BulkEntry(int number, string name, string sourceSuffix) =>
        new(number, null, null, null, null, null, null, HorseName: name,
            HorseSourceIdentity: SourceIdentity(sourceSuffix));

    private static string SourceIdentity(string suffix) =>
        $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002036{suffix}/00";

    private static async Task RegisterEntryAsync(HttpClient client, string raceId, string horseId, int? number)
    {
        using var registered = await client.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horseId, number, null, null, null, null, null, null, null, null,
                EntryId: DeterministicIdGenerator.BuildRaceEntryId(raceId, horseId)));
        registered.EnsureSuccessStatusCode();
    }

    private static async Task<(string Fingerprint, long Generation)> GetFenceAsync(HttpClient client, string raceId)
    {
        using var response = await client.GetAsync($"/api/admin/races/{raceId}/entry-repair/assignment-fence");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.GetProperty("assignmentFingerprint").GetString()!,
            document.RootElement.GetProperty("generation").GetInt64());
    }

    private static async Task<HttpResponseMessage> PostOddsAsync(HttpClient client, string raceId,
        RecordRaceOddsSnapshotRequest request, (string Fingerprint, long Generation) fence)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/races/{raceId}/odds-snapshots")
        {
            Content = JsonContent.Create(request),
        };
        AddFence(message, fence);
        return await client.SendAsync(message);
    }

    private static void AddFence(HttpRequestMessage message, (string Fingerprint, long Generation) fence)
    {
        message.Headers.Add("X-Race-Assignment-Fingerprint", fence.Fingerprint);
        message.Headers.Add("X-Race-Hold-Generation", fence.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
