using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using HorseRacingPrediction.Contracts.Collection;

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
            Assert.AreEqual(first.Outcomes[0].TaskId, replay.Outcomes[0].TaskId);
            Assert.HasCount(1, await store.GetTasksAsync());

            var distinctBatch = request with
            {
                BatchId = "race-subjects:recovered-parent",
                Items = [request.Items[0]],
            };
            var distinctBatchReplay = await PostAsync(client, distinctBatch);
            Assert.AreEqual("Reused", distinctBatchReplay.Outcomes.Single().Status);
            Assert.AreEqual(first.Outcomes[0].RequestId, distinctBatchReplay.Outcomes.Single().RequestId);
            Assert.AreEqual(first.Outcomes[0].TaskId, distinctBatchReplay.Outcomes.Single().TaskId);
            Assert.HasCount(1, await store.GetTasksAsync(),
                "A different batch identity must still return the same ordinary discovery task receipt.");

            var changedReplay = request with
            {
                Items =
                [
                    request.Items[0] with { Attributes = new Dictionary<string, string> { ["startTime"] = "16:00" } },
                    request.Items[1],
                ],
            };
            var mismatch = await PostAsync(client, changedReplay);
            Assert.AreEqual("Rejected", mismatch.Outcomes[0].Status);
            Assert.AreEqual("IdempotencyMismatch", mismatch.Outcomes[0].ErrorCode);
            Assert.IsNull(mismatch.Outcomes[0].RequestId);
            Assert.HasCount(1, await store.GetTasksAsync(),
                "A changed fingerprint must reject without rebinding or duplicating the accepted request.");

            var duplicate = request with { Items = [request.Items[0], request.Items[0]] };
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
                    new CreateCollectionTaskBatchRequest(new CreateCollectionTaskBatchInputDto("ExplicitItems",
                        ExplicitItems: duplicate)))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
                    new CreateCollectionTaskBatchRequest(new CreateCollectionTaskBatchInputDto("ExplicitItems",
                        PreviewSelection: new("horse-profile", 1, CollectionReason.Discovery, "SpecificResources"),
                        ExplicitItems: request)))).StatusCode,
                "A merged batch request must not combine selector modes.");
            Assert.HasCount(1, await store.GetTasksAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<CollectionRequestBulkResponse> PostAsync(HttpClient client,
        CollectionRequestBulkRequest request)
    {
        var response = await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
            new CreateCollectionTaskBatchRequest(new CreateCollectionTaskBatchInputDto("ExplicitItems",
                ExplicitItems: request)));
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadFromJsonAsync<CreateCollectionTaskBatchResponse>();
        var submission = responseBody?.Submission;
        Assert.IsNotNull(submission);
        Assert.AreEqual("ExplicitItems", submission.Mode);
        return submission.ExplicitItems!;
    }
}
