namespace HorseRacingPrediction.Collector.Http;

public sealed class CollectionLeaseHttpContext
{
    private readonly AsyncLocal<Lease?> _current = new();
    public Lease? Current => _current.Value;

    public IDisposable Begin(string jobId, string leaseToken)
    {
        var previous = _current.Value;
        _current.Value = new Lease(jobId, leaseToken);
        return new Scope(() => _current.Value = previous);
    }

    public sealed record Lease(string JobId, string LeaseToken);
    private sealed class Scope(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose(); }
    }
}

public sealed class CollectionLeaseHeaderHandler(CollectionLeaseHttpContext context) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var lease = context.Current;
        if (lease is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Collection-Job-Id", lease.JobId);
            request.Headers.TryAddWithoutValidation("X-Collection-Lease-Token", lease.LeaseToken);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
