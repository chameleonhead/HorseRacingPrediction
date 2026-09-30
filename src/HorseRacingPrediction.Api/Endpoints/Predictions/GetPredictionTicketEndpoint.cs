using EventFlow.EntityFramework;
using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class GetPredictionTicketEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/predictions/{predictionTicketId}",
                    [SwaggerOperation(Summary = "Get prediction ticket", Description = "Returns prediction ticket read model")]
        async (string predictionTicketId, IQueryProcessor queryProcessor, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        var query = new ReadModelByIdQuery<PredictionTicketReadModel>(predictionTicketId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.PredictionTicketId))
                            return Results.NotFound();

                        using var dbContext = dbContextProvider.CreateContext();
                        var race = string.IsNullOrWhiteSpace(readModel.RaceId) ? null : await queryProcessor.ProcessAsync(
                            new ReadModelByIdQuery<RacePredictionContextReadModel>(readModel.RaceId), cancellationToken).ConfigureAwait(false);
                        var horseNames = await dbContext.Horses.AsNoTracking().ToDictionaryAsync(x => x.HorseId, x => x.RegisteredName, cancellationToken).ConfigureAwait(false);
                        var entries = race?.Entries.ToDictionary(x => x.EntryId, x => x.HorseId, StringComparer.Ordinal) ?? [];
                        var response = new PredictionTicketDto(
                            readModel.PredictionTicketId,
                            readModel.RaceId,
                            readModel.PredictorType,
                            readModel.PredictorId,
                            readModel.ConfidenceScore,
                            readModel.SummaryComment,
                            readModel.PredictedAt,
                            readModel.Marks
                                .Select(x => new PredictionMarkDto(x.EntryId, x.MarkCode, x.PredictedRank, x.Score, x.Comment,
                                    entries.GetValueOrDefault(x.EntryId),
                                    entries.TryGetValue(x.EntryId, out var horseId) ? horseNames.GetValueOrDefault(horseId) : null))
                                .ToList(),
                            (HorseRacingPrediction.Contracts.Predictions.TicketStatus)(int)readModel.TicketStatus,
                            (HorseRacingPrediction.Contracts.Predictions.EvaluationStatus)(int)readModel.EvaluationStatus,
                            race?.RaceName, race?.RaceDate, race?.RacecourseCode, race?.RaceNumber);

                        return Results.Ok(response);
                    })
                    .WithName("GetPredictionTicket")
                    .WithTags("Prediction API")
                    .Produces<PredictionTicketDto>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
