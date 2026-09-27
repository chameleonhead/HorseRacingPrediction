using HorseRacingPrediction.PredictionScheduling;
namespace HorseRacingPrediction.Api.Endpoints.PredictionScheduling;

internal static class EnqueuePredictionCandidatesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/prediction-candidates", async (EnqueuePredictionCandidatesRequest request,
            IPredictionSchedule schedule, CancellationToken token) =>
        {
            if (request.RaceIds is null || request.RaceIds.Length == 0 || request.RaceIds.Any(string.IsNullOrWhiteSpace))
                return Results.BadRequest(new { message = "RaceIds must contain at least one non-empty ID." });
            await schedule.EnqueueAsync(request.RaceIds, request.Now, token);
            return Results.Accepted();
        });
}
