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
    public async Task CanonicalResourceWithoutDomainAttribute_IsProtected_AndCompletedLeaseCannotWrite()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var date = new DateOnly(2026, 9, 26);
        var raceId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildRaceId(date, "中山", 5);
        (await http.PostAsJsonAsync("/api/races", new { raceId, raceDate = date, racecourseCode = "中山", raceNumber = 5, raceName = "lease検証" })).EnsureSuccessStatusCode();
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("race-detail");
        await store.RegisterDefinitionAsync(definition, "detail", ResourceType.Race, 4, "test", true);
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(new(ResourceType.Race, "JRA", "20260926:Nakayama:5"), definition, 4,
            CollectionReason.Initial, now, effectiveDate: date);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var weather = new { observedAt = now, conditionCode = "SUNNY" };
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/races/{raceId}/weather", weather)).StatusCode);
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        http.DefaultRequestHeaders.Add("X-Collection-Task-Id", lease.TaskId.ToString());
        http.DefaultRequestHeaders.Add("X-Collection-Lease-Token", lease.LeaseToken);
        (await http.PostAsJsonAsync($"/api/races/{raceId}/weather", weather)).EnsureSuccessStatusCode();
        Assert.IsTrue(await store.CompleteAttemptAsync(lease.TaskId, lease.LeaseToken, now.AddSeconds(1), new(CollectionAttemptResult.Succeeded)));
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/races/{raceId}/weather", weather)).StatusCode);
    }

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
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken, raceId));
        Assert.IsFalse(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, "spoofed", raceId));
    }
}
