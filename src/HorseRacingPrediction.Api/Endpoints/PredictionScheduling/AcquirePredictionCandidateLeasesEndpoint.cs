using HorseRacingPrediction.Contracts.PredictionScheduling;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.PredictionScheduling;

internal static class AcquirePredictionCandidateLeasesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/prediction-candidate-leases", async (AcquirePredictionCandidateLeasesRequest request,
            [FromServices] IPredictionSchedule schedule, CancellationToken token) =>
        {
            var acquisition = request.Acquisition;
            if (acquisition is null || acquisition.MaxCount < 1 || acquisition.LeaseDuration <= TimeSpan.Zero || acquisition.MinAge < TimeSpan.Zero)
                return Results.BadRequest(new { message = "Lease count, duration, and age are invalid." });
            var leases = await schedule.AcquireAsync(acquisition.Now, acquisition.MinAge, acquisition.MaxCount, acquisition.LeaseDuration, token);
            return leases.Count == 0 ? Results.NoContent() : Results.Ok(new AcquirePredictionCandidateLeasesResponse(
                leases.Select(lease => new PredictionCandidateLeaseDto(lease.RaceId, lease.LeaseToken)).ToArray()));
        })
        .Produces<AcquirePredictionCandidateLeasesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest);
}
