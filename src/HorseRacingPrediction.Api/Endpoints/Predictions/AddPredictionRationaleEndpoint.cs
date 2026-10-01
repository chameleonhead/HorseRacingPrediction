using EventFlow;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class AddPredictionRationaleEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/predictions/{predictionTicketId}/rationales",
                    [SwaggerOperation(Summary = "Add prediction rationale", Description = "Appends a rationale entry to prediction ticket")]
        async (string predictionTicketId, AddPredictionRationaleRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        if (request.Rationale is not { } rationale)
                            return Results.BadRequest(new[] { "Command execution failed." });

                        var command = new AddPredictionRationaleCommand(
                            new PredictionTicketId(predictionTicketId),
                            rationale.SubjectType,
                            rationale.SubjectId,
                            rationale.SignalType,
                            rationale.SignalValue,
                            rationale.ExplanationText);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("AddPredictionRationale")
                    .WithTags("Prediction API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
