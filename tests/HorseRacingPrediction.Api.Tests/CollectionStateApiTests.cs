using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.AspNetCore.TestHost;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionStateApiTests
{
    [TestMethod]
    public async Task WorkerProxy_EnqueuesAndAcquiresTask_FromApiOwnedStore()
    {
        var (app, _) = await TestApplicationFactory.CreateAsync();
        await using var appScope = app;
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var proxy = HttpProcessingStateStoreProxy.Create(client);
        var now = DateTimeOffset.UtcNow;

        await proxy.EnqueueJobAsync("test-job", "test-key", "{\"value\":1}", now);
        var acquired = await proxy.AcquireReadyJobsAsync(
            "test-job", now, TimeSpan.Zero, 1, TimeSpan.FromMinutes(5));

        Assert.AreEqual(1, acquired.Count);
        Assert.AreEqual("test-key", acquired[0].DeduplicationKey);
        Assert.AreEqual("{\"value\":1}", acquired[0].Payload);
    }
}
