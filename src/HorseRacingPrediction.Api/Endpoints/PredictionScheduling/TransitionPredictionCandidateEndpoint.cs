using HorseRacingPrediction.Contracts.PredictionScheduling;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.PredictionScheduling;

internal static class TransitionPredictionCandidateEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPatch(
        "/api/v2/internal/prediction-candidates/{raceId}", async (string raceId,
            HorseRacingPrediction.Contracts.PredictionScheduling.TransitionPredictionCandidateRequest request, [FromServices] IPredictionSchedule schedule,
            CancellationToken token) =>
        {
            var transition = request.Transition;
            if (transition is null || string.IsNullOrWhiteSpace(transition.Mode) || string.IsNullOrWhiteSpace(transition.LeaseToken))
                return Results.BadRequest(new { message = "Mode must be Complete or Requeue." });
            if (transition.Mode == "Complete")
            {
                if (transition.AvailableAt is not null || transition.Error is not null)
                    return Results.BadRequest(new { message = "Complete mode does not accept availableAt or error." });
                return await schedule.CompleteAsync(raceId, transition.LeaseToken, token)
                    ? Results.NoContent() : Results.Conflict();
            }
            if (transition.Mode == "Requeue")
            {
                if (transition.AvailableAt is null)
                    return Results.BadRequest(new { message = "Requeue mode requires availableAt." });
                return await schedule.RequeueAsync(raceId, transition.LeaseToken, transition.AvailableAt.Value, transition.Error, token)
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
