using System.Net;
using System.Net.Http;
using HorseRacingPrediction.Collector.Http;

namespace HorseRacingPrediction.Collector.Tests.Http;

[TestClass]
public sealed class TransientBadGatewayRetryHandlerTests
{
    [TestMethod]
    public async Task SendAsync_WhenBadGatewayThenSuccess_RetriesAndReturnsSuccess()
    {
        var inner = new SequencedHandler(
            new HttpResponseMessage(HttpStatusCode.BadGateway),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        var handler = new TransientBadGatewayRetryHandler(TimeSpan.Zero) { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var response = await client.PostAsync("/api/test", new StringContent("body"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(2, inner.Requests.Count);
        // 各リトライで本文が正しく再送されていること。
        Assert.IsTrue(inner.Requests.All(x => x.Content is not null));
    }

    [TestMethod]
    public async Task SendAsync_WhenAlwaysBadGateway_RetriesTwiceThenReturnsLastFailure()
    {
        var inner = new SequencedHandler(
            new HttpResponseMessage(HttpStatusCode.BadGateway),
            new HttpResponseMessage(HttpStatusCode.BadGateway),
            new HttpResponseMessage(HttpStatusCode.BadGateway));
        var handler = new TransientBadGatewayRetryHandler(TimeSpan.Zero) { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var response = await client.PostAsync("/api/test", new StringContent("body"));

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        // 初回 + リトライ2回 = 合計3回。
        Assert.AreEqual(3, inner.Requests.Count);
    }

    [TestMethod]
    public async Task SendAsync_WhenNotFound_DoesNotRetry()
    {
        var inner = new SequencedHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var handler = new TransientBadGatewayRetryHandler(TimeSpan.Zero) { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var response = await client.GetAsync("/api/test");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(1, inner.Requests.Count);
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<HttpRequestMessage> Requests { get; } = [];

        public SequencedHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
