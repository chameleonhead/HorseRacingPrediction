using HorseRacingPrediction.Contracts.PredictionScheduling;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.PredictionScheduling;

internal static class EnqueuePredictionCandidatesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/prediction-candidates", async (HorseRacingPrediction.Contracts.PredictionScheduling.EnqueuePredictionCandidatesRequest request,
            [FromServices] IPredictionSchedule schedule, CancellationToken token) =>
        {
            var candidates = request.Candidates;
            if (candidates?.RaceIds is null || candidates.RaceIds.Length == 0 || candidates.RaceIds.Any(string.IsNullOrWhiteSpace))
                return Results.BadRequest(new { message = "RaceIds must contain at least one non-empty ID." });
            await schedule.EnqueueAsync(candidates.RaceIds, candidates.Now, token);
            return Results.Accepted();
        });
}
