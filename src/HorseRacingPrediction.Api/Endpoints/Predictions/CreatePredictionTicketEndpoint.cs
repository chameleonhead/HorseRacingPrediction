using EventFlow;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Infrastructure.Persistence;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class CreatePredictionTicketEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/predictions",
                    [SwaggerOperation(Summary = "Create prediction ticket", Description = "Creates one prediction ticket for a race")]
        async (CreatePredictionTicketRequest request, ICommandBus commandBus, RaceWriteCoordinator coordinator, CancellationToken cancellationToken) =>
                    {
                        var predictionTicketId = string.IsNullOrWhiteSpace(request.PredictionTicketId)
                            ? PredictionTicketId.New : new PredictionTicketId(request.PredictionTicketId);
                        var command = new CreatePredictionTicketCommand(
                            predictionTicketId,
                            request.RaceId,
                            request.PredictorType,
                            request.PredictorId,
                            request.ConfidenceScore,
                            request.SummaryComment,
                            await coordinator.AssignmentFingerprintAsync(request.RaceId, cancellationToken));

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Created($"/api/predictions/{predictionTicketId.Value}", new { PredictionTicketId = predictionTicketId.Value })
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("CreatePredictionTicket")
                    .WithTags("Prediction API")
                    .Produces(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
