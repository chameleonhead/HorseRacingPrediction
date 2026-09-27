using HorseRacingPrediction.Api.CollectionController;
namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class ApplyRaceEntryRepairEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/races/{raceId}/entry-repairs", RaceEntryRepairOperations.ApplyAsync);
}
