using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class PredictionScheduleEndpointTests
{
    [TestMethod]
    public async Task HttpContract_PreservesDeduplicationDisjointLeasesFencingAndTransitions()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        using var unauthenticated = app.GetTestClient();
        var now = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await unauthenticated.PostAsJsonAsync("/api/v2/internal/prediction-candidates",
                new EnqueuePredictionCandidatesRequest(["race-unauthorized"], now))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/v2/internal/prediction-candidates",
                new EnqueuePredictionCandidatesRequest([], now))).StatusCode);

        var enqueue = new EnqueuePredictionCandidatesRequest(["race-1", "race-2", "race-1"], now);
        Assert.AreEqual(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/v2/internal/prediction-candidates", enqueue)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/v2/internal/prediction-candidates", enqueue)).StatusCode,
            "Repeated enqueue should coalesce by race ID rather than duplicate candidates.");

        var acquire = new AcquirePredictionCandidatesRequest(now, TimeSpan.Zero, 1, TimeSpan.FromMinutes(5));
        var concurrent = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v2/internal/prediction-candidate-leases", acquire),
            client.PostAsJsonAsync("/api/v2/internal/prediction-candidate-leases", acquire));
        var leases = new List<PredictionCandidateLease>();
        foreach (var response in concurrent)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            leases.AddRange((await response.Content.ReadFromJsonAsync<List<PredictionCandidateLease>>())!);
            response.Dispose();
        }
        Assert.HasCount(2, leases);
        Assert.AreEqual(2, leases.Select(x => x.RaceId).Distinct(StringComparer.Ordinal).Count(),
            "Concurrent HTTP acquisition calls must be disjoint.");

        var first = leases[0];
        var candidatePath = $"/api/v2/internal/prediction-candidates/{Uri.EscapeDataString(first.RaceId)}";
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync(candidatePath,
                new PredictionCandidateTransitionRequest("Complete", "stale-token"))).StatusCode,
            "A stale lease token must not transition the candidate.");

        var availableAt = now.AddMinutes(15);
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync(candidatePath,
                new PredictionCandidateTransitionRequest("Requeue", first.LeaseToken, availableAt,
                    "temporary failure"))).StatusCode);
        var beforeAvailability = await client.PostAsJsonAsync("/api/v2/internal/prediction-candidate-leases",
            acquire with { Now = availableAt.AddMinutes(-1), MaxCount = 2 });
        Assert.AreEqual(HttpStatusCode.NoContent, beforeAvailability.StatusCode);
        beforeAvailability.Dispose();

        var afterAvailability = await client.PostAsJsonAsync("/api/v2/internal/prediction-candidate-leases",
            acquire with { Now = availableAt, MaxCount = 2 });
        Assert.AreEqual(HttpStatusCode.OK, afterAvailability.StatusCode);
        var reacquired = await afterAvailability.Content.ReadFromJsonAsync<List<PredictionCandidateLease>>();
        Assert.IsNotNull(reacquired);
        var requeuedLease = reacquired.Single(x => x.RaceId == first.RaceId);
        Assert.AreNotEqual(first.LeaseToken, requeuedLease.LeaseToken);
        var completePath = $"/api/v2/internal/prediction-candidates/{Uri.EscapeDataString(requeuedLease.RaceId)}";
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync(completePath,
                new PredictionCandidateTransitionRequest("Complete", requeuedLease.LeaseToken))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync(completePath,
                new PredictionCandidateTransitionRequest("Complete", requeuedLease.LeaseToken))).StatusCode,
            "A repeated completed transition is rejected deterministically after the lease has been consumed.");
        afterAvailability.Dispose();
    }
}
