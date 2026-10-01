using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Races;
namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class GetRaceEntryRepairHoldEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(
        "/api/v2/admin/races/{raceId}/entry-repair/hold-state", RaceEntryRepairOperations.GetHoldAsync)
        .Produces<GetRaceEntryRepairHoldResponse>(StatusCodes.Status200OK);
}
