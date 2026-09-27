using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class CloseRaceLifecycleEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/close",
                    [SwaggerOperation(Summary = "Close race lifecycle", Description = "Closes the race lifecycle from ResultDeclared or PayoutDeclared state")]
        async (string raceId, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new CloseRaceLifecycleCommand(new RaceId(raceId));
                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("CloseRaceLifecycle")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
