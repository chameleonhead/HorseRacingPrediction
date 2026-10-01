using EventFlow;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class RecalculatePredictionEvaluationEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/predictions/{predictionTicketId}/recalculate-evaluation",
                    [SwaggerOperation(Summary = "Recalculate prediction evaluation", Description = "Recalculates evaluation of a prediction ticket with updated data")]
        async (string predictionTicketId, RecalculatePredictionEvaluationRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        if (request.Evaluation is not { } evaluation)
                            return Results.BadRequest(new[] { "Command execution failed." });

                        var command = new RecalculatePredictionEvaluationCommand(
                            new PredictionTicketId(predictionTicketId),
                            evaluation.RaceId,
                            evaluation.EvaluatedAt,
                            evaluation.EvaluationRevision,
                            evaluation.HitTypeCodes,
                            evaluation.ScoreSummary,
                            evaluation.ReturnAmount,
                            evaluation.Roi);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("RecalculatePredictionEvaluation")
                    .WithTags("Prediction API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
