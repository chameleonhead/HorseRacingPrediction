using EventFlow;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class CreateRaceEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races",
                    [SwaggerOperation(Summary = "Create race", Description = "Creates a race aggregate in Draft state")]
        async (CreateRaceRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var raceId = string.IsNullOrWhiteSpace(request.RaceId) ? RaceId.New : new RaceId(request.RaceId);
                            var command = new CreateRaceCommand(
                                raceId,
                                request.RaceDate,
                                request.RacecourseCode,
                                request.RaceNumber,
                                request.RaceName,
                                gradeCode: request.GradeCode,
                                surfaceCode: request.SurfaceCode,
                                distanceMeters: request.DistanceMeters,
                                directionCode: request.DirectionCode);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Created($"/api/races/{raceId.Value}", new { RaceId = raceId.Value })
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Race is already created.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("CreateRace")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
