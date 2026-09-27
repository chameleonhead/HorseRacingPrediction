using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Api.Security;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.MachineLearning;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.MachineLearning;


internal static class GetMlPredictionEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/races/{raceId}/ml-prediction",
                    [SwaggerOperation(Summary = "ML予測", Description = "ML.NETモデルを使って出走馬の予測着順を返します。訓練済みモデルがない場合は統計スコアで代替します。")]
        async (string raceId, IQueryProcessor queryProcessor, IRacePredictor predictor, RaceWriteCoordinator coordinator, CancellationToken cancellationToken) =>
                    {
                        if (await coordinator.ReadBarrierAsync(raceId, cancellationToken) is { Verified: false })
                            return Results.Conflict(new { code = "RaceRepairPending" });
                        var raceQuery = new ReadModelByIdQuery<AppReadModels.RacePredictionContextReadModel>(raceId);
                        var raceContext = await queryProcessor.ProcessAsync(raceQuery, cancellationToken).ConfigureAwait(false);

                        if (raceContext is null || string.IsNullOrEmpty(raceContext.RaceId))
                            return Results.NotFound();

                        var result = await predictor.PredictAsync(
                            raceContext,
                            async (horseId, ct) => await queryProcessor.ProcessAsync(
                                new ReadModelByIdQuery<AppReadModels.HorseRaceHistoryReadModel>(horseId), ct).ConfigureAwait(false),
                            async (jockeyId, ct) => await queryProcessor.ProcessAsync(
                                new ReadModelByIdQuery<AppReadModels.JockeyRaceHistoryReadModel>(jockeyId), ct).ConfigureAwait(false),
                            cancellationToken).ConfigureAwait(false);

                        var response = new ApiContracts.MlPredictionResponse(
                            result.RaceId,
                            result.Rankings.Select(r => new ApiContracts.MlHorsePrediction(
                                r.EntryId, r.HorseId, r.HorseNumber, r.PredictedScore, r.PredictedRank)).ToList());

                        return Results.Ok(response);
                    })
                    .AddEndpointFilter<RacePredictionReadEndpointFilter>()
                    .WithName("GetMlPrediction")
                    .WithTags("Race API")
                    .Produces<ApiContracts.MlPredictionResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
