using EventFlow.EntityFramework;
using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.MachineLearning;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.MachineLearning;


internal static class TrainMlModelEndpoint
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/ml/train",
                    [SwaggerOperation(Summary = "ML再訓練", Description = "過去レース結果を使ってML.NETモデルを再訓練します。")]
        async (IQueryProcessor queryProcessor, IDbContextProvider<EventStoreDbContext> dbContextProvider,
                        IRacePredictor predictor, CancellationToken cancellationToken) =>
                    {
                        using var dbContext = dbContextProvider.CreateContext();
                        var allRaces = await dbContext.Set<RaceResultViewReadModel>()
                            .AsNoTracking()
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var finishedRaces = allRaces.Where(r => r.Status == HorseRacingPrediction.Domain.Races.RaceStatus.ResultDeclared).ToList();

                        if (finishedRaces.Count == 0)
                            return Results.BadRequest(new[] { "訓練に使用できる完了済みレースがありません。" });

                        await predictor.TrainAsync(
                            finishedRaces,
                            async (raceId, ct) => await queryProcessor.ProcessAsync(
                                new ReadModelByIdQuery<AppReadModels.RacePredictionContextReadModel>(raceId), ct).ConfigureAwait(false),
                            async (horseId, ct) => await queryProcessor.ProcessAsync(
                                new ReadModelByIdQuery<AppReadModels.HorseRaceHistoryReadModel>(horseId), ct).ConfigureAwait(false),
                            async (jockeyId, ct) => await queryProcessor.ProcessAsync(
                                new ReadModelByIdQuery<AppReadModels.JockeyRaceHistoryReadModel>(jockeyId), ct).ConfigureAwait(false),
                            cancellationToken).ConfigureAwait(false);

                        return Results.Ok(new { TrainedRaceCount = finishedRaces.Count, IsModelTrained = predictor.IsModelTrained });
                    })
                    .WithName("TrainMlModel")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status400BadRequest);
    }
}
