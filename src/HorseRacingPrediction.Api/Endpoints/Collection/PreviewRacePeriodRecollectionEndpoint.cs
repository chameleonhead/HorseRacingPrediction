using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class PreviewRacePeriodRecollectionEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/recollection-previews", (PreviewRacePeriodRecollectionRequest request) =>
        {
            if (request.Period is null)
                return Results.BadRequest(new { message = "Period input is required." });
            var input = request.Period;
            var selector = new CreateRacePeriodRecollectionRequest(input.From, input.To, input.Provider, input.BatchId);
            var error = CollectionPlatformEndpointSupport.ValidateRacePeriodRecollection(selector);
            return error is null
                ? Results.Ok(new PreviewRacePeriodRecollectionResponse(new(input.From, input.To,
                    input.To.DayNumber - input.From.DayNumber + 1, input.Provider.Trim().ToUpperInvariant())))
                : Results.BadRequest(new { message = error });
        })
        .WithName("PreviewRacePeriodRecollection")
        .WithTags("Collection Platform")
        .Produces<PreviewRacePeriodRecollectionResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
}
