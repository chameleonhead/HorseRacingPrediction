using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Collector.CollectionPlatform;
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
        Assert.AreEqual("/api/admin/collection/requests/batch", handler.RequestUri!.AbsolutePath);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.IsNotNull(handler.Body);
        Assert.AreEqual(3, handler.Body.Items.Count);
        Assert.AreEqual(3, handler.Body.Items.Select(x => x.ItemKey).Distinct(StringComparer.Ordinal).Count());
        Assert.HasCount(3, response.Outcomes);
    }

    private static CollectionRequestBulkItem Item(string key, string type, string id, string definition) =>
        new(key, type, "JRA", id, definition, 1, "Discovery", "Realtime", 70, null,
            new DateOnly(2026, 9, 12), null);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public CollectionRequestBulkRequest? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            Method = request.Method;
            Body = await request.Content!.ReadFromJsonAsync<CollectionRequestBulkRequest>(cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CollectionRequestBulkResponse(
                    Body!.Items.Select(x => new CollectionRequestBulkOutcome(x.ItemKey, "Created")).ToArray())),
            };
        }
    }
}
