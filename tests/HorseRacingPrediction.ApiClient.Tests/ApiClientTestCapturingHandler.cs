using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace HorseRacingPrediction.ApiClient.Tests;

internal sealed class ApiClientTestCapturingHandler(
    Func<ApiClientTestCapturedRequest, CancellationToken, Task<ApiClientTestCapturedResponse>> responseFactory) : HttpMessageHandler
{
    private readonly ConcurrentQueue<ApiClientTestCapturedRequest> requests = new();
    public IReadOnlyCollection<ApiClientTestCapturedRequest> Requests => requests.ToArray();
    public bool ObservedCancellation { get; private set; }

    public ApiClientTestCapturingHandler(Func<ApiClientTestCapturedRequest, ApiClientTestCapturedResponse> responseFactory)
        : this((request, _) => Task.FromResult(responseFactory(request))) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var captured = new ApiClientTestCapturedRequest(request.Method, request.RequestUri,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        requests.Enqueue(captured);
        ApiClientTestCapturedResponse result;
        try
        {
            result = await responseFactory(captured, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObservedCancellation = true;
            throw;
        }

        var response = new HttpResponseMessage(result.StatusCode)
        {
            Content = result.StatusCode == HttpStatusCode.NoContent || result.Body.Length == 0
                ? null
                : new StringContent(result.Body, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
        if (result.Location is not null)
            response.Headers.Location = new Uri(result.Location, UriKind.Relative);
        return response;
    }
}
