using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Races;
namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class PreviewRaceEntryRepairEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/races/{raceId}/entry-repair-previews", RaceEntryRepairOperations.PreviewAsync)
        .Produces<PreviewRaceEntryRepairResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
}
