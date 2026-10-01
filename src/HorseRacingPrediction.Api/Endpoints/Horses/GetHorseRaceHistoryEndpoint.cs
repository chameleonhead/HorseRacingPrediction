using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Api.Security;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

using static HorseRacingPrediction.Api.Endpoints.Horses.HorseEndpointMappings;


internal static class GetHorseRaceHistoryEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/horses/{horseId}/race-history",
                    [SwaggerOperation(Summary = "Get horse race history", Description = "Returns race history read model for a horse")]
        async ([AsParameters] ApiContracts.Horses.GetHorseRaceHistoryRequest request, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
                    {
                        var horseId = request.HorseId;
                        var query = new ReadModelByIdQuery<AppReadModels.HorseRaceHistoryReadModel>(horseId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.HorseId))
                            return Results.NotFound();

                        return Results.Ok(new ApiContracts.Horses.GetHorseRaceHistoryResponse(ToAgentHorseRaceHistory(readModel)));
                    })
                    .AddEndpointFilter<RacePredictionReadEndpointFilter>()
                    .WithName("GetHorseRaceHistory")
                    .WithTags("Horse API")
                    .Produces<ApiContracts.Horses.GetHorseRaceHistoryResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
