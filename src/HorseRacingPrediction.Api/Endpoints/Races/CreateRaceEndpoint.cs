using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class CreateRaceEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races",
                    [SwaggerOperation(Summary = "Create race", Description = "Creates a race aggregate in Draft state")]
        async (CreateRaceRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var race = request?.Race;
                        if (race is null) return Results.BadRequest(new[] { "Race is required." });
                        try
                        {
                            var raceId = string.IsNullOrWhiteSpace(race.RaceId) ? RaceId.New : new RaceId(race.RaceId);
                            var command = new CreateRaceCommand(
                                raceId,
                                race.RaceDate,
                                race.RacecourseCode,
                                race.RaceNumber,
                                race.RaceName,
                                gradeCode: race.GradeCode,
                                surfaceCode: race.SurfaceCode,
                                distanceMeters: race.DistanceMeters,
                                directionCode: race.DirectionCode);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Created($"/api/races/{raceId.Value}", new CreateRaceResponse(raceId.Value))
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Race is already created.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("CreateRace")
                    .WithTags("Race API")
                    .Produces<CreateRaceResponse>(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
