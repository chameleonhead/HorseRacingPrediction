using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class StartRaceEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/start",
                    [SwaggerOperation(Summary = "Start race", Description = "Moves race lifecycle from PreRaceOpen to InProgress")]
        async (string raceId, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new StartRaceCommand(new RaceId(raceId));
                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("StartRace")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
