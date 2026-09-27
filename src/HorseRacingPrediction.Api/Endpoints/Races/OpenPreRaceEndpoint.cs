using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class OpenPreRaceEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/open-pre-race",
                    [SwaggerOperation(Summary = "Open pre-race", Description = "Moves race lifecycle from CardPublished to PreRaceOpen")]
        async (string raceId, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new OpenPreRaceCommand(new RaceId(raceId));
                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("OpenPreRace")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
