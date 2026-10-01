namespace HorseRacingPrediction.ApiClient.Tests;

internal sealed class ApiClientTestMarkerHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Test-Handler", "configured");
        return base.SendAsync(request, cancellationToken);
    }
}
