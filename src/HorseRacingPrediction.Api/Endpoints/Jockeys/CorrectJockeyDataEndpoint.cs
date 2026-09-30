using EventFlow;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Domain.Jockeys;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Jockeys;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

internal static class CorrectJockeyDataEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/jockeys/{jockeyId}",
                    [SwaggerOperation(Summary = "Correct jockey data", Description = "Corrects jockey master data with an optional audit reason")]
        async (string jockeyId, CorrectJockeyDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new CorrectJockeyDataCommand(
                            new JockeyId(jockeyId),
                            request.DisplayName,
                            request.NormalizedName,
                            request.AffiliationCode,
                            request.Reason);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("CorrectJockeyData")
                    .WithTags("Jockey API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
