using System.Net.Http.Json;
using HorseRacingPrediction.Contracts.PredictionScheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HorseRacingPrediction.PredictionScheduling;

public sealed class HttpPredictionSchedule(HttpClient client) : IPredictionSchedule
{
    public async Task EnqueueAsync(IEnumerable<string> raceIds, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/v2/internal/prediction-candidates",
            new HorseRacingPrediction.Contracts.PredictionScheduling.EnqueuePredictionCandidatesRequest(
                new EnqueuePredictionCandidatesInputDto(raceIds.ToArray(), now)), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<PredictionCandidateLease>> AcquireAsync(DateTimeOffset now, TimeSpan minAge,
        int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/v2/internal/prediction-candidate-leases",
            new AcquirePredictionCandidateLeasesRequest(new AcquirePredictionCandidatesInputDto(now, minAge, maxCount, leaseDuration)), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return [];
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AcquirePredictionCandidateLeasesResponse>(cancellationToken)
            .ConfigureAwait(false);
        return payload?.Leases.Select(lease => new PredictionCandidateLease(lease.RaceId, lease.LeaseToken)).ToArray() ?? [];
    }

    public Task<bool> CompleteAsync(string raceId, string leaseToken, CancellationToken cancellationToken = default)
        => PatchResultAsync(raceId, new HorseRacingPrediction.Contracts.PredictionScheduling.TransitionPredictionCandidateRequest(
            raceId, new PredictionCandidateTransitionInputDto("Complete", leaseToken)), cancellationToken);

    public Task<bool> RequeueAsync(string raceId, string leaseToken, DateTimeOffset availableAt, string? error,
        CancellationToken cancellationToken = default)
        => PatchResultAsync(raceId, new HorseRacingPrediction.Contracts.PredictionScheduling.TransitionPredictionCandidateRequest(
            raceId, new PredictionCandidateTransitionInputDto("Requeue", leaseToken, availableAt, error)), cancellationToken);

    private async Task<bool> PatchResultAsync(string raceId, object request, CancellationToken cancellationToken)
    {
        using var response = await client.PatchAsJsonAsync($"api/v2/internal/prediction-candidates/{Uri.EscapeDataString(raceId)}", request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return true;
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict || response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
}
