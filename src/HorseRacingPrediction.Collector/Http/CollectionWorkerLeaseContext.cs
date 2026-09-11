namespace HorseRacingPrediction.Collector.Http;

public sealed record CollectionWorkerLease(Guid TaskId, string LeaseToken);

public static class CollectionWorkerLeaseContext
{
    private static readonly AsyncLocal<CollectionWorkerLease?> CurrentLease = new();
    public static CollectionWorkerLease? Current => CurrentLease.Value;

    public static IDisposable Push(Guid taskId, string leaseToken)
    {
        var previous = CurrentLease.Value;
        CurrentLease.Value = new(taskId, leaseToken);
        return new Scope(() => CurrentLease.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

public sealed class CollectionWorkerLeaseHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (CollectionWorkerLeaseContext.Current is { } lease)
        {
            request.Headers.TryAddWithoutValidation("X-Collection-Task-Id", lease.TaskId.ToString("D"));
            request.Headers.TryAddWithoutValidation("X-Collection-Lease-Token", lease.LeaseToken);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
