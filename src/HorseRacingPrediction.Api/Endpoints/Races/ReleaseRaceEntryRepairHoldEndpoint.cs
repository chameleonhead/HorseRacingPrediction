using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class ReleaseRaceEntryRepairHoldEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPatch("/api/v2/admin/races/{raceId}/entry-repair/hold", RaceEntryRepairOperations.PatchHoldAsync)
            .WithName("ReleaseRaceEntryRepairHold").WithTags("Race Entry Repair")
            .Produces<ReleaseRaceEntryRepairHoldResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);
    }
}
