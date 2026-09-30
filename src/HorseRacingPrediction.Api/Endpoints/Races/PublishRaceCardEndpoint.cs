using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class PublishRaceCardEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/card/publish",
                    [SwaggerOperation(Summary = "Publish race card", Description = "Moves lifecycle from Draft to CardPublished")]
        async (string raceId, PublishRaceCardRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var command = new PublishRaceCardCommand(new RaceId(raceId), request.EntryCount);
                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Ok()
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Race card can only be published from Draft state.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("PublishRaceCard")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
