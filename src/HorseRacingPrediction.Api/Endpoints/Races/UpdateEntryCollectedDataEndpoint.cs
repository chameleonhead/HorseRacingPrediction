using EventFlow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class UpdateEntryCollectedDataEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/races/{raceId}/entries/{entryId}",
                    [SwaggerOperation(Summary = "Update collected entry data", Description = "Adds or refreshes owner and declared body weight collected from a race card")]
        async (string raceId, string entryId, UpdateEntryCollectedDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var command = new UpdateEntryCollectedDataCommand(
                                new RaceId(raceId), entryId,
                                request.DeclaredWeight, request.DeclaredWeightDiff, request.OwnerName);
                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Ok(new { RaceId = raceId, EntryId = entryId })
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("UpdateEntryCollectedData")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
