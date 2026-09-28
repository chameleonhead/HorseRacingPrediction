using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionRequestBatchEndpointTests
{
    [TestMethod]
    public async Task BatchRequest_ReturnsPerItemOutcomes_AndIsIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"request-batch-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse,
                1, "initial", false);
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
            var request = new CollectionRequestBulkRequest("race-subjects:race-1",
            [
                new("Horse:H001", "Horse", "JRA", "H001", "horse-profile", 1,
                    "Discovery", "Realtime", 70, null, new(2026, 9, 12), null),
                new("Horse:H002", "Horse", "JRA", "H002", "missing-definition", 1,
                    "Discovery", "Realtime", 70, null, new(2026, 9, 12), null),
            ]);

            var first = await PostAsync(client, request);
            var replay = await PostAsync(client, request);

            CollectionAssert.AreEqual(new[] { "Created", "Rejected" },
                first.Outcomes.Select(x => x.Status).ToArray());
            CollectionAssert.AreEqual(new[] { "Reused", "Rejected" },
                replay.Outcomes.Select(x => x.Status).ToArray());
            Assert.AreEqual(first.Outcomes[0].RequestId, replay.Outcomes[0].RequestId);
            Assert.HasCount(1, await store.GetTasksAsync());

            var duplicate = request with { Items = [request.Items[0], request.Items[0]] };
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
                    new CollectionTaskBatchRequest("ExplicitItems", ExplicitItems: duplicate))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
                    new CollectionTaskBatchRequest("ExplicitItems", PreviewSelection:
                        new("horse-profile", 1, CollectionReason.Discovery, "SpecificResources"),
                        ExplicitItems: request))).StatusCode,
                "A merged batch request must not combine selector modes.");
            Assert.HasCount(1, await store.GetTasksAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<CollectionRequestBulkResponse> PostAsync(HttpClient client,
        CollectionRequestBulkRequest request)
    {
        var response = await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
            new CollectionTaskBatchRequest("ExplicitItems", ExplicitItems: request));
        response.EnsureSuccessStatusCode();
        var submission = await response.Content.ReadFromJsonAsync<CollectionTaskBatchSubmissionResponse>();
        Assert.IsNotNull(submission);
        Assert.AreEqual("ExplicitItems", submission.Mode);
        return submission.ExplicitItems!;
    }
}
