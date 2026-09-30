using EventFlow;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Domain.Jockeys;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Jockeys;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

internal static class UpdateJockeyProfileEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/jockeys/{jockeyId}",
                    [SwaggerOperation(Summary = "Update jockey profile", Description = "Updates profile information of an existing jockey")]
        async (string jockeyId, UpdateJockeyProfileRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new UpdateJockeyProfileCommand(
                            new JockeyId(jockeyId),
                            request.DisplayName,
                            request.NormalizedName,
                            request.AffiliationCode);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("UpdateJockeyProfile")
                    .WithTags("Jockey API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
