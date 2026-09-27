using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.PredictionScheduling;

internal static class TransitionPredictionCandidateEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPatch(
        "/api/v2/internal/prediction-candidates/{raceId}", async (string raceId,
            PredictionCandidateTransitionRequest request, [FromServices] IPredictionSchedule schedule,
            CancellationToken token) =>
        {
            if (request.Mode == "Complete")
            {
                if (request.AvailableAt is not null || request.Error is not null)
                    return Results.BadRequest(new { message = "Complete mode does not accept availableAt or error." });
                return await schedule.CompleteAsync(raceId, request.LeaseToken, token)
                    ? Results.NoContent() : Results.Conflict();
            }
            if (request.Mode == "Requeue")
            {
                if (request.AvailableAt is null)
                    return Results.BadRequest(new { message = "Requeue mode requires availableAt." });
                return await schedule.RequeueAsync(raceId, request.LeaseToken, request.AvailableAt.Value, request.Error, token)
                    ? Results.NoContent() : Results.Conflict();
            }
            return Results.BadRequest(new { message = "Mode must be Complete or Requeue." });
        })
        .WithName("TransitionPredictionCandidate")
        .WithTags("Prediction Scheduling")
        .WithDescription("mode is Complete or Requeue; Requeue requires availableAt and Complete rejects it.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
}
