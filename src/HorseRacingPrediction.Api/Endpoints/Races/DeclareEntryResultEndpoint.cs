using EventFlow;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class DeclareEntryResultEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/entries/{entryId}/result",
                    [SwaggerOperation(Summary = "Declare entry result", Description = "Declares finish result for a specific entry after race result is declared")]
        async (string raceId, string entryId, DeclareEntryResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var command = new DeclareEntryResultCommand(
                                new RaceId(raceId),
                                entryId,
                                request.FinishPosition,
                                request.OfficialTime,
                                request.MarginText,
                                request.LastThreeFurlongTime,
                                request.AbnormalResultCode,
                                request.PrizeMoney,
                                request.CornerPositions);

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
                    .WithName("DeclareEntryResult")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
