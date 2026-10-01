using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Races;
namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class ApplyRaceEntryRepairEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/races/{raceId}/entry-repairs", RaceEntryRepairOperations.ApplyAsync)
        .Produces<ApplyRaceEntryRepairResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
}
