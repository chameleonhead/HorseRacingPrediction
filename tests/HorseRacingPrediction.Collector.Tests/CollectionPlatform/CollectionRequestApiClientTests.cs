using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionRequestApiClientTests
{
    [TestMethod]
    public async Task RequestManyAsync_SendsOneBatchPost()
    {
        var handler = new RecordingHandler();
        var client = new CollectionRequestApiClient(new HttpClient(handler)
        {
            BaseAddress = new("https://api.example.test/"),
        });
        var request = new CollectionRequestBulkRequest("race-subjects:race-1",
        [
            Item("Horse:H001", "Horse", "H001", "horse-profile"),
            Item("Jockey:J001", "Jockey", "J001", "jockey-profile"),
            Item("Trainer:T001", "Trainer", "T001", "trainer-profile"),
        ]);

        var response = await client.RequestManyAsync(request, CancellationToken.None);

        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual("/api/v2/admin/collection/task-batches", handler.RequestUri!.AbsolutePath);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.IsNotNull(handler.Body);
        Assert.AreEqual("ExplicitItems", handler.Body.Mode);
        Assert.IsNotNull(handler.Body.ExplicitItems);
        Assert.AreEqual(3, handler.Body.ExplicitItems.Items.Count);
        Assert.AreEqual(3, handler.Body.ExplicitItems.Items.Select(x => x.ItemKey).Distinct(StringComparer.Ordinal).Count());
        Assert.HasCount(3, response.Outcomes);
    }

    [TestMethod]
    public async Task RequestManyAsync_PreservesOrderedPartialOutcomesAndErrors()
    {
        var handler = new RecordingHandler(returnPartialOutcomes: true);
        var client = new CollectionRequestApiClient(new HttpClient(handler)
        {
            BaseAddress = new("https://api.example.test/"),
        });
        var request = new CollectionRequestBulkRequest("race-subjects:race-2",
        [
            Item("Horse:H001", "Horse", "H001", "horse-profile"),
            Item("Jockey:J001", "Jockey", "J001", "jockey-profile"),
            Item("Trainer:T001", "Trainer", "T001", "trainer-profile"),
        ]);

        var response = await client.RequestManyAsync(request, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Created", "Rejected", "Held" },
            response.Outcomes.Select(x => x.Status).ToArray());
        Assert.AreEqual("Jockey:J001", response.Outcomes[1].ItemKey);
        Assert.AreEqual("DefinitionMissing", response.Outcomes[1].ErrorCode);
        Assert.AreEqual("missing-definition", response.Outcomes[1].Message);
        Assert.IsFalse(response.Outcomes[2].CreatedTask);
    }

    private static CollectionRequestBulkItem Item(string key, string type, string id, string definition) =>
        new(key, type, "JRA", id, definition, 1, "Discovery", "Realtime", 70, null,
            new DateOnly(2026, 9, 12), null);

    private sealed class RecordingHandler(bool returnPartialOutcomes = false) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public CollectionTaskBatchRequest? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            Method = request.Method;
            Body = await request.Content!.ReadFromJsonAsync<CollectionTaskBatchRequest>(cancellationToken);
            var items = Body!.ExplicitItems!.Items;
            var outcomes = returnPartialOutcomes
                ? new[]
                {
                    new CollectionRequestBulkOutcome(items[0].ItemKey, "Created",
                        Guid.NewGuid(), Guid.NewGuid(), true),
                    new CollectionRequestBulkOutcome(items[1].ItemKey, "Rejected",
                        ErrorCode: "DefinitionMissing", Message: "missing-definition"),
                    new CollectionRequestBulkOutcome(items[2].ItemKey, "Held"),
                }
                : items.Select(x => new CollectionRequestBulkOutcome(x.ItemKey, "Created")).ToArray();
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CollectionTaskBatchSubmissionResponse("ExplicitItems", null,
                    new CollectionRequestBulkResponse(outcomes))),
            };
        }
    }
}
