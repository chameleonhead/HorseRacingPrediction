using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Races;
namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class UpdateRaceEntryRepairHoldEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/v2/admin/races/{raceId}/entry-repair/hold", RaceEntryRepairOperations.PutHoldAsync)
            .WithName("UpdateRaceEntryRepairHold").WithTags("Race Entry Repair")
            .Produces<UpdateRaceEntryRepairHoldResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);
    }
}
