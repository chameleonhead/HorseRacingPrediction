using EventFlow;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Contracts.Predictions;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class FinalizePredictionTicketEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/predictions/{predictionTicketId}/finalize",
                    [SwaggerOperation(Summary = "Finalize prediction ticket", Description = "Moves prediction ticket from Draft to Finalized")]
        async ([AsParameters] FinalizePredictionTicketRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var predictionTicketId = request.PredictionTicketId;
                        var command = new FinalizePredictionTicketCommand(new PredictionTicketId(predictionTicketId));
                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("FinalizePredictionTicket")
                    .WithTags("Prediction API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
