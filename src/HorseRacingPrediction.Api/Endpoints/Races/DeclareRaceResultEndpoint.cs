using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class DeclareRaceResultEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/result",
                    [SwaggerOperation(Summary = "Declare race result", Description = "Declares result and moves lifecycle to ResultDeclared")]
        async (string raceId, DeclareRaceResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var command = new DeclareRaceResultCommand(
                                new RaceId(raceId),
                                request.WinningHorseName,
                                request.DeclaredAt ?? HorseRacingPrediction.Contracts.Common.Time.JstTime.Now());

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Ok()
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("DeclareRaceResult")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
