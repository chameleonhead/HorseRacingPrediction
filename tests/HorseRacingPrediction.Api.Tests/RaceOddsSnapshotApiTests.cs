using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Queries.ReadModels;

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
        using var created = await client.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new(2026, 9, 12), "東京", 11, "test", raceId));
        created.EnsureSuccessStatusCode();
        var firstAt = new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero);
        var secondAt = firstAt.AddMinutes(2);
        foreach (var request in new[]
        {
            new RecordRaceOddsSnapshotRequest(firstAt, [new(1, 2.5m, 1)]),
            new RecordRaceOddsSnapshotRequest(secondAt, [new(1, 2.3m, 1)]),
        })
        {
            using var response = await client.PostAsJsonAsync($"/api/admin/races/{raceId}/odds-snapshots", request);
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
    }

    [TestMethod]
    public async Task SnapshotRetainsMultipleMarketsAndSelections()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        const string raceId = "race-00000000-0000-0000-0000-000000000012";
        using var created = await client.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new(2026, 9, 12), "東京", 12, "markets", raceId));
        created.EnsureSuccessStatusCode();
        var observedAt = new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero);
        var request = new RecordRaceOddsSnapshotRequest(observedAt, [new(1, 2.5m, 1)],
        [
            new("Win", "1", 2.5m, 1),
            new("Quinella", "1-3", 8.4m, 2),
            new("Trio", "1-3-7", 14.2m, 4),
        ]);
        using var response = await client.PostAsJsonAsync($"/api/admin/races/{raceId}/odds-snapshots", request);
        response.EnsureSuccessStatusCode();

        var snapshots = await client.GetFromJsonAsync<List<RaceOddsSnapshot>>(
            $"/api/admin/races/{raceId}/odds-snapshots");
        Assert.IsNotNull(snapshots);
        Assert.HasCount(1, snapshots);
        Assert.HasCount(3, snapshots[0].Observations!);
        Assert.AreEqual("1-3", snapshots[0].Observations![1].Selection);
        Assert.AreEqual(8.4m, snapshots[0].Observations![1].Value);
    }
}
