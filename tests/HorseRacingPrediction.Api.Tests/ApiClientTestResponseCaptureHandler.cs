using System.Net;

namespace HorseRacingPrediction.Api.Tests;

internal sealed class ApiClientTestResponseCaptureHandler : DelegatingHandler
{
    public string? LastResponseBody { get; private set; }
    public HttpStatusCode? LastStatusCode { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatusCode = response.StatusCode;
        LastResponseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        return response;
    }
}
