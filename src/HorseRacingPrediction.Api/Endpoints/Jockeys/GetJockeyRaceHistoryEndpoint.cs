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
        async ([AsParameters] ApiContracts.Jockeys.GetJockeyRaceHistoryRequest request, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
                    {
                        var jockeyId = request.JockeyId;
                        var query = new ReadModelByIdQuery<AppReadModels.JockeyRaceHistoryReadModel>(jockeyId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.JockeyId))
                            return Results.NotFound();

                        return Results.Ok(new ApiContracts.Jockeys.GetJockeyRaceHistoryResponse(ToAgentJockeyRaceHistory(readModel)));
                    })
                    .AddEndpointFilter<RacePredictionReadEndpointFilter>()
                    .WithName("GetJockeyRaceHistory")
                    .WithTags("Jockey API")
                    .Produces<ApiContracts.Jockeys.GetJockeyRaceHistoryResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
