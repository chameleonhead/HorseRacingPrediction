using HorseRacingPrediction.Collector.CollectionPlatform;

namespace HorseRacingPrediction.Collector.Http;

internal sealed class CollectionRuntimeTimingHandler : DelegatingHandler
{
    private readonly ICollectionTaskTelemetryClock _clock;

    public CollectionRuntimeTimingHandler() : this(SystemCollectionTaskTelemetryClock.Instance) { }

    internal CollectionRuntimeTimingHandler(ICollectionTaskTelemetryClock clock) => _clock = clock;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var started = _clock.GetTimestamp();
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CollectionRuntimeTimingContext.RecordInternalApi(
                _clock.GetElapsedTime(started, _clock.GetTimestamp()));
        }
    }
}
