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
}
