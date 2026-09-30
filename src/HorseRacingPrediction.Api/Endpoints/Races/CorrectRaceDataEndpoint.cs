using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class CorrectRaceDataEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/races/{raceId}",
                    [SwaggerOperation(Summary = "Correct race data", Description = "Corrects race metadata such as name, racecourse, grade, surface or distance")]
        async (string raceId, CorrectRaceDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var race = request?.Race;
                        if (race is null) return Results.BadRequest(new[] { "Race is required." });
                        var command = new CorrectRaceDataCommand(
                            new RaceId(raceId),
                            race.RaceName,
                            race.RacecourseCode,
                            race.RaceNumber,
                            race.GradeCode,
                            race.SurfaceCode,
                            race.DistanceMeters,
                            race.DirectionCode,
                            race.Reason);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("CorrectRaceData")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
