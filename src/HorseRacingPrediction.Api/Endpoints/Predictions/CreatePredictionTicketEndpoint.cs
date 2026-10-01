using EventFlow;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Infrastructure.Persistence;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class CreatePredictionTicketEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/predictions",
                    [SwaggerOperation(Summary = "Create prediction ticket", Description = "Creates one prediction ticket for a race")]
        async (CreatePredictionTicketRequest request, ICommandBus commandBus, RaceWriteCoordinator coordinator, CancellationToken cancellationToken) =>
                    {
                        if (request.Ticket is not { } ticket)
                            return Results.BadRequest(new[] { "Command execution failed." });

                        var predictionTicketId = string.IsNullOrWhiteSpace(ticket.PredictionTicketId)
                            ? PredictionTicketId.New : new PredictionTicketId(ticket.PredictionTicketId);
                        var command = new CreatePredictionTicketCommand(
                            predictionTicketId,
                            ticket.RaceId,
                            ticket.PredictorType,
                            ticket.PredictorId,
                            ticket.ConfidenceScore,
                            ticket.SummaryComment,
                            await coordinator.AssignmentFingerprintAsync(ticket.RaceId, cancellationToken));

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Created($"/api/predictions/{predictionTicketId.Value}", new CreatePredictionTicketResponse(predictionTicketId.Value))
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("CreatePredictionTicket")
                    .WithTags("Prediction API")
                    .Produces<CreatePredictionTicketResponse>(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
