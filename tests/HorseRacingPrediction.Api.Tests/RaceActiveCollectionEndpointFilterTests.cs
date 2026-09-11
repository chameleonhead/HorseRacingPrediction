using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class RaceActiveCollectionEndpointFilterTests
{
    [TestMethod]
    public async Task RaceMutation_WithActiveCollectionTask_ReturnsConflict()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var _ = app;
        using var __ = client;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("race-card");
        var race = new ResourceKey(ResourceType.RaceCard, "jra", "race-filter-test");
        await store.RegisterDefinitionAsync(definition, "Race card", ResourceType.RaceCard,
            1, "Initial", false);
        await store.RequestAsync(race, definition, 1, CollectionReason.Initial, DateTimeOffset.UtcNow);

        var response = await client.PostAsJsonAsync(
            "/api/races/race-filter-test/card/publish", new { EntryCount = 1 });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task WorkerBooleanHeader_DoesNotBypassActiveCollection()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var _ = app;
        using var __ = client;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        client.DefaultRequestHeaders.Add("X-Collection-Worker", "true");
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("race-card");
        await store.RegisterDefinitionAsync(definition, "Race card", ResourceType.RaceCard, 1, "Initial", false);
        await store.RequestAsync(new(ResourceType.RaceCard, "jra", "race-filter-spoof"), definition, 1,
            CollectionReason.Initial, DateTimeOffset.UtcNow);

        var response = await client.PostAsJsonAsync(
            "/api/races/race-filter-spoof/card/publish", new { EntryCount = 1 });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task BodyAddressedRaceMutation_WithActiveCollectionTask_ReturnsConflict()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var _ = app;
        using var __ = client;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("race-result");
        await store.RegisterDefinitionAsync(definition, "Race result", ResourceType.RaceResult, 1, "Initial", false);
        await store.RequestAsync(new(ResourceType.RaceResult, "jra", "race-filter-body"), definition, 1,
            CollectionReason.Initial, DateTimeOffset.UtcNow);

        var response = await client.PostAsJsonAsync("/api/races/result-bulk",
            new { TargetRaceId = "race-filter-body", RefreshExistingData = true });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task MatchingActiveLease_IsValidated()
    {
        const string raceId = "race-00000000-0000-0000-0000-000000000099";
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var _ = app;
        using var __ = client;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("race-card");
        await store.RegisterDefinitionAsync(definition, "Race card", ResourceType.RaceCard, 1, "Initial", false);
        var receipt = await store.RequestAsync(new(ResourceType.RaceCard, "jra", raceId),
            definition, 1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken, raceId));
        Assert.IsFalse(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, "spoofed", raceId));
    }
}
