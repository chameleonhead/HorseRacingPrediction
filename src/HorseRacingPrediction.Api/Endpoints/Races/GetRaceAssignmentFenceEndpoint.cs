using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Races;
namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class GetRaceAssignmentFenceEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(
        "/api/v2/admin/races/{raceId}/entry-repair/assignment-fence-state", RaceEntryRepairOperations.GetFenceAsync)
        .Produces<GetRaceAssignmentFenceResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status409Conflict);
}
