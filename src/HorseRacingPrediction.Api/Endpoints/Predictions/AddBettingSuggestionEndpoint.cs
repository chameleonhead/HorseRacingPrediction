using EventFlow;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class AddBettingSuggestionEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/predictions/{predictionTicketId}/betting-suggestions",
                    [SwaggerOperation(Summary = "Add betting suggestion", Description = "Appends a betting suggestion to prediction ticket")]
        async (string predictionTicketId, AddBettingSuggestionRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new AddBettingSuggestionCommand(
                            new PredictionTicketId(predictionTicketId),
                            request.BetTypeCode,
                            request.SelectionExpression,
                            request.StakeAmount,
                            request.ExpectedValue);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("AddBettingSuggestion")
                    .WithTags("Prediction API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
