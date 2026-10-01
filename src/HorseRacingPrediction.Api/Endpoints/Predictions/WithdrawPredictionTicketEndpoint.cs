using EventFlow;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class WithdrawPredictionTicketEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/predictions/{predictionTicketId}/withdraw",
                    [SwaggerOperation(Summary = "Withdraw prediction ticket", Description = "Withdraws a prediction ticket with an optional reason")]
        async (string predictionTicketId, WithdrawPredictionTicketRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        if (request.Withdrawal is not { } withdrawal)
                            return Results.BadRequest(new[] { "Command execution failed." });

                        var command = new WithdrawPredictionTicketCommand(
                            new PredictionTicketId(predictionTicketId),
                            withdrawal.Reason);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("WithdrawPredictionTicket")
                    .WithTags("Prediction API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
