using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Predictor.Scheduling;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionClientContractIntegrationTests
{
    [TestMethod]
    public async Task CollectionRequestApiClient_SingleAndBatchRequestsReachWrappedResourceEndpoints()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"collection-client-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse,
                3, "initial", false);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<ICollectionDispatchTelemetry, NullCollectionDispatchTelemetry>();
            using var domain = new SqliteDbContextProvider();
            builder.Services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(domain);
            var app = builder.Build();
            app.MapCollectionApiV2Endpoints();
            await app.StartAsync();
            await using var lifetime = app;
            using var client = app.GetTestClient();
            var requestClient = new CollectionRequestApiClient(client);
            var attributes = new Dictionary<string, string> { ["requestedByRaceId"] = "race-client-1" };

            await requestClient.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H-CLIENT-001"),
                new("horse-profile"), 3, CollectionReason.Discovery, CollectionLane.Realtime, 77,
                new Uri("https://example.test/horse/H-CLIENT-001"), new DateOnly(2026, 9, 12), attributes,
                CancellationToken.None);

            var single = (await store.GetTasksAsync()).Single();
            Assert.AreEqual(CollectionResourceType.Horse, single.Resource.Type);
            Assert.AreEqual("JRA", single.Resource.Provider);
            Assert.AreEqual("H-CLIENT-001", single.Resource.Id);
            Assert.AreEqual("horse-profile", single.Definition.Value);
            Assert.AreEqual(3, single.RequestedRevision);
            Assert.AreEqual(CollectionLane.Realtime, single.Lane);
            Assert.AreEqual(77, single.Priority);
            var singleDispatch = (await store.GetPendingDispatchesAsync(DateTimeOffset.UtcNow, 10))
                .Single(x => x.Notification.TaskId == single.TaskId);
            Assert.AreEqual(new DateOnly(2026, 9, 12), singleDispatch.EffectiveDate);
            Assert.AreEqual("race-client-1", singleDispatch.Attributes!["requestedByRaceId"]);

            var batch = await requestClient.RequestManyAsync(new CollectionRequestBulkRequest("client-batch-1",
            [
                new("H-CLIENT-002", "Horse", "JRA", "H-CLIENT-002", "horse-profile", 3,
                    "Discovery", "Realtime", 66, null, new DateOnly(2026, 9, 13), attributes),
            ]), CancellationToken.None);

            Assert.AreEqual("Created", batch.Outcomes.Single().Status);
            Assert.AreEqual(2, (await store.GetTasksAsync()).Count);
            var batched = (await store.GetTasksAsync()).Single(x => x.Resource.Id == "H-CLIENT-002");
            Assert.AreEqual(3, batched.RequestedRevision);
            Assert.AreEqual(66, batched.Priority);
            var batchDispatch = (await store.GetPendingDispatchesAsync(DateTimeOffset.UtcNow, 10))
                .Single(x => x.Notification.TaskId == batched.TaskId);
            Assert.AreEqual(new DateOnly(2026, 9, 13), batchDispatch.EffectiveDate);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CollectionReadinessClient_UnwrapsReadinessResponseFromApi()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"collection-readiness-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse,
                1, "initial", false);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H-READINESS"),
                new("horse-profile"), 1, CollectionReason.Discovery, DateTimeOffset.UtcNow,
                CollectionLane.Normal, 10, null, effectiveDate: new DateOnly(2026, 9, 12),
                attributes: new Dictionary<string, string> { ["requestedByRaceId"] = "race-readiness-1" });

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<ICollectionDispatchTelemetry, NullCollectionDispatchTelemetry>();
            var app = builder.Build();
            app.MapCollectionApiV2Endpoints();
            await app.StartAsync();
            await using var lifetime = app;
            using var client = app.GetTestClient();
            var readiness = await new CollectionReadinessClient(client).GetAsync("race-readiness-1");

            Assert.AreEqual(1, readiness.PendingHorseRequests);
            Assert.AreEqual(0, readiness.PendingJockeyRequests);
            Assert.AreEqual(0, readiness.PendingRaceResultRequests);
            Assert.AreEqual(0, readiness.PendingTrainerRequests);
            Assert.AreEqual(1, readiness.TotalPendingRequests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
