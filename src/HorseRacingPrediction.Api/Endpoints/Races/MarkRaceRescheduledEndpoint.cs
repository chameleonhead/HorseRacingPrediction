using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class MarkRaceRescheduledEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/reschedule",
                    async (string raceId, MarkRaceRescheduledRequest request, ICommandBus commandBus,
                        CancellationToken cancellationToken) =>
                    {
                        var reschedule = request?.Reschedule;
                        if (reschedule is null) return Results.BadRequest(new[] { "Reschedule is required." });
                        try
                        {
                            var command = new MarkRaceRescheduledCommand(new RaceId(raceId), reschedule.ReplacementRaceId);
                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess ? Results.Ok() : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("MarkRaceRescheduled")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
