using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HorseRacingPrediction.PredictionScheduling;

public sealed class HttpPredictionSchedule(HttpClient client) : IPredictionSchedule
{
    public async Task EnqueueAsync(IEnumerable<string> raceIds, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/internal/prediction-schedule/enqueue",
            new EnqueuePredictionCandidatesRequest(raceIds.ToArray(), now), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<PredictionCandidateLease>> AcquireAsync(DateTimeOffset now, TimeSpan minAge,
        int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/internal/prediction-schedule/acquire",
            new AcquirePredictionCandidatesRequest(now, minAge, maxCount, leaseDuration), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PredictionCandidateLease>>(cancellationToken)
                   .ConfigureAwait(false) ?? [];
    }

    public Task<bool> CompleteAsync(string raceId, string leaseToken, CancellationToken cancellationToken = default)
        => PostResultAsync("complete", new CompletePredictionCandidateRequest(raceId, leaseToken), cancellationToken);

    public Task<bool> RequeueAsync(string raceId, string leaseToken, DateTimeOffset availableAt, string? error,
        CancellationToken cancellationToken = default)
        => PostResultAsync("requeue", new RequeuePredictionCandidateRequest(raceId, leaseToken, availableAt, error), cancellationToken);

    private async Task<bool> PostResultAsync(string action, object request, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync($"api/internal/prediction-schedule/{action}", request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<bool>(cancellationToken).ConfigureAwait(false);
    }
}

public static class PredictionScheduleEndpointExtensions
{
    public static IEndpointRouteBuilder MapPredictionScheduleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/internal/prediction-schedule");
        group.MapPost("/enqueue", async (EnqueuePredictionCandidatesRequest request, IPredictionSchedule schedule,
            CancellationToken token) => { await schedule.EnqueueAsync(request.RaceIds, request.Now, token); return Results.Accepted(); });
        group.MapPost("/acquire", (AcquirePredictionCandidatesRequest request, IPredictionSchedule schedule,
            CancellationToken token) => schedule.AcquireAsync(request.Now, request.MinAge, request.MaxCount, request.LeaseDuration, token));
        group.MapPost("/complete", (CompletePredictionCandidateRequest request, IPredictionSchedule schedule,
            CancellationToken token) => schedule.CompleteAsync(request.RaceId, request.LeaseToken, token));
        group.MapPost("/requeue", (RequeuePredictionCandidateRequest request, IPredictionSchedule schedule,
            CancellationToken token) => schedule.RequeueAsync(request.RaceId, request.LeaseToken, request.AvailableAt, request.Error, token));
        return endpoints;
    }
}

public sealed record EnqueuePredictionCandidatesRequest(string[] RaceIds, DateTimeOffset Now);
public sealed record AcquirePredictionCandidatesRequest(DateTimeOffset Now, TimeSpan MinAge, int MaxCount, TimeSpan LeaseDuration);
public sealed record CompletePredictionCandidateRequest(string RaceId, string LeaseToken);
public sealed record RequeuePredictionCandidateRequest(string RaceId, string LeaseToken, DateTimeOffset AvailableAt, string? Error);
