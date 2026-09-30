using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Api.Security;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

using static HorseRacingPrediction.Api.Endpoints.Jockeys.JockeyEndpointMappings;


internal static class GetJockeyRaceHistoryEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/jockeys/{jockeyId}/race-history",
                    [SwaggerOperation(Summary = "Get jockey race history", Description = "Returns race history read model for a jockey")]
        async (string jockeyId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
                    {
                        var query = new ReadModelByIdQuery<AppReadModels.JockeyRaceHistoryReadModel>(jockeyId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.JockeyId))
                            return Results.NotFound();

                        return Results.Ok(ToAgentJockeyRaceHistory(readModel));
                    })
                    .AddEndpointFilter<RacePredictionReadEndpointFilter>()
                    .WithName("GetJockeyRaceHistory")
                    .WithTags("Jockey API")
                    .Produces<HorseRacingPrediction.Contracts.Jockeys.JockeyRaceHistoryDto>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
