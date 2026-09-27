using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class GetPredictionComparisonEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/races/{raceId}/comparison",
                    [SwaggerOperation(Summary = "Get prediction comparison view", Description = "Returns prediction vs result comparison for a race")]
        async (string raceId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
                    {
                        var query = new ReadModelByIdQuery<PredictionComparisonViewReadModel>(raceId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.RaceId))
                            return Results.NotFound();

                        return Results.Ok(readModel);
                    })
                    .WithName("GetPredictionComparison")
                    .WithTags("Race API")
                    .Produces<PredictionComparisonViewReadModel>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
