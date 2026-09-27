using HorseRacingPrediction.PredictionScheduling;
namespace HorseRacingPrediction.Api.Endpoints.PredictionScheduling;

internal static class AcquirePredictionCandidateLeasesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/prediction-candidate-leases", async (AcquirePredictionCandidatesRequest request,
            IPredictionSchedule schedule, CancellationToken token) =>
        {
            if (request.MaxCount < 1 || request.LeaseDuration <= TimeSpan.Zero || request.MinAge < TimeSpan.Zero)
                return Results.BadRequest(new { message = "Lease count, duration, and age are invalid." });
            var leases = await schedule.AcquireAsync(request.Now, request.MinAge, request.MaxCount, request.LeaseDuration, token);
            return leases.Count == 0 ? Results.NoContent() : Results.Ok(leases);
        });
}
