using HorseRacingPrediction.ApiClient.Collection;
using HorseRacingPrediction.Contracts.Collection;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

namespace HorseRacingPrediction.ApiClient.Tests;

internal sealed class ApiClientTestRecordingHandler(bool delayUntilCanceled = false) : HttpMessageHandler
{
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();
    public ConcurrentQueue<string?> Bodies { get; } = new();
    public bool ObservedCancellation { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        Bodies.Enqueue(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));
        if (delayUntilCanceled)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObservedCancellation = true;
                throw;
            }
        }
        return SuccessResponse();
    }

    public static HttpResponseMessage SuccessResponse() => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new GetCollectionPipelineResponse(
            new CollectionPipelineStateDto(false, null, DateTimeOffset.Parse("2026-10-01T00:00:00+09:00"))))
    };
}
