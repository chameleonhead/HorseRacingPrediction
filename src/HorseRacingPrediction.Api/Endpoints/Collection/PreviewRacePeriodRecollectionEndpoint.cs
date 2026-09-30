using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class PreviewRacePeriodRecollectionEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/recollection-previews", (CreateRacePeriodRecollectionRequest request) =>
        {
            var error = CollectionPlatformEndpointSupport.ValidateRacePeriodRecollection(request);
            return error is null ? Results.Ok(new RacePeriodRecollectionPreview(request.From, request.To,
                request.To.DayNumber - request.From.DayNumber + 1, request.Provider.Trim().ToUpperInvariant()))
                : Results.BadRequest(new { message = error });
        });
}
