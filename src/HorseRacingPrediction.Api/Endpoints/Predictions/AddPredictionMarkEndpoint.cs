using EventFlow;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class AddPredictionMarkEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/predictions/{predictionTicketId}/marks",
                    [SwaggerOperation(Summary = "Add prediction mark", Description = "Appends a mark record to prediction ticket")]
        async (string predictionTicketId, AddPredictionMarkRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new AddPredictionMarkCommand(
                            new PredictionTicketId(predictionTicketId),
                            request.EntryId,
                            request.MarkCode,
                            request.PredictedRank,
                            request.Score,
                            request.Comment);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("AddPredictionMark")
                    .WithTags("Prediction API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
