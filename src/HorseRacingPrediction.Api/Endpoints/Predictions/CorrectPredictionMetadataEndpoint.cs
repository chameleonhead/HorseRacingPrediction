using EventFlow;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class CorrectPredictionMetadataEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/predictions/{predictionTicketId}",
                    [SwaggerOperation(Summary = "Correct prediction metadata", Description = "Corrects confidence score or summary comment of a prediction ticket")]
        async (string predictionTicketId, CorrectPredictionMetadataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new CorrectPredictionMetadataCommand(
                            new PredictionTicketId(predictionTicketId),
                            request.ConfidenceScore,
                            request.SummaryComment,
                            request.Reason);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("CorrectPredictionMetadata")
                    .WithTags("Prediction API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
