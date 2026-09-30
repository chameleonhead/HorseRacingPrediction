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
                        var command = new CorrectRaceDataCommand(
                            new RaceId(raceId),
                            request.RaceName,
                            request.RacecourseCode,
                            request.RaceNumber,
                            request.GradeCode,
                            request.SurfaceCode,
                            request.DistanceMeters,
                            request.DirectionCode,
                            request.Reason);

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
