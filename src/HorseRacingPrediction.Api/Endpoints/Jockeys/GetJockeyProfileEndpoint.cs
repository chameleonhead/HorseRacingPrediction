using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

using static HorseRacingPrediction.Api.Endpoints.Jockeys.JockeyEndpointMappings;


internal static class GetJockeyProfileEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/jockeys/{jockeyId}",
                    [SwaggerOperation(Summary = "Get jockey profile", Description = "Returns jockey profile read model")]
        async (string jockeyId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
                    {
                        var query = new ReadModelByIdQuery<AppReadModels.JockeyReadModel>(jockeyId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.JockeyId))
                            return Results.NotFound();

                        return Results.Ok(ToAgentJockey(readModel));
                    })
                    .WithName("GetJockeyProfile")
                    .WithTags("Jockey API")
                    .Produces<ApiContracts.JockeyDto>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
