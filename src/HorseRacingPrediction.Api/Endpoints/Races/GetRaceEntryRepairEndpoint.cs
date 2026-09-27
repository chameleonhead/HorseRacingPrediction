using HorseRacingPrediction.Api.CollectionController;
namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class GetRaceEntryRepairEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(
        "/api/v2/admin/races/{raceId}/entry-repair/inspection", RaceEntryRepairOperations.GetInspectionAsync);
}
