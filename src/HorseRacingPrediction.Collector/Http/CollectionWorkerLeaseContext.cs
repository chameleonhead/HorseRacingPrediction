namespace HorseRacingPrediction.Collector.Http;

public sealed record CollectionWorkerLease(Guid TaskId, string LeaseToken, long RaceHoldGeneration, string? AssignmentFingerprint);

public static class CollectionWorkerLeaseContext
{
    private static readonly AsyncLocal<CollectionWorkerLease?> CurrentLease = new();
    public static CollectionWorkerLease? Current => CurrentLease.Value;

    public static IDisposable Push(Guid taskId, string leaseToken, long raceHoldGeneration = 0, string? assignmentFingerprint = null)
    {
        var previous = CurrentLease.Value;
        CurrentLease.Value = new(taskId, leaseToken, raceHoldGeneration, assignmentFingerprint);
        return new Scope(() => CurrentLease.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

public sealed class CollectionWorkerLeaseHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (CollectionWorkerLeaseContext.Current is { } lease)
        {
            request.Headers.TryAddWithoutValidation("X-Collection-Task-Id", lease.TaskId.ToString("D"));
            request.Headers.TryAddWithoutValidation("X-Collection-Lease-Token", lease.LeaseToken);
            request.Headers.TryAddWithoutValidation("X-Race-Hold-Generation", lease.RaceHoldGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (lease.AssignmentFingerprint is not null)
                request.Headers.TryAddWithoutValidation("X-Race-Assignment-Fingerprint", lease.AssignmentFingerprint);
        }
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            try
            {
                using var payload = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (payload.RootElement.TryGetProperty("code", out var code)
                    && code.GetString() is "RaceRepairHeld" or "StaleRaceAssignmentFence")
                {
                    response.Dispose();
                    throw new HorseRacingPrediction.Contracts.CollectionRepairHeldException();
                }
            }
            catch (System.Text.Json.JsonException) { }
        }
        return response;
    }
}
