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
}
