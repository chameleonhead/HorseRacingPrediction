using HorseRacingPrediction.Api.CollectionController;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class ReleaseRaceEntryRepairHoldEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPatch("/api/v2/admin/races/{raceId}/entry-repair/hold", RaceEntryRepairOperations.PatchHoldAsync)
            .WithName("ReleaseRaceEntryRepairHold").WithTags("Race Entry Repair");
    }
}
