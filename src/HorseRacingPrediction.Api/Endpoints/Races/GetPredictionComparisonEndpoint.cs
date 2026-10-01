using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Contracts.Races;
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

                        return Results.Ok(new GetPredictionComparisonResponse(new PredictionComparisonDto(
                            readModel.RaceId, readModel.RaceName, readModel.WinningHorseName, readModel.ResultDeclaredAt,
                            readModel.PredictionTickets.Select(ticket => new PredictionComparisonTicketDto(
                                ticket.PredictionTicketId, ticket.PredictorType, ticket.PredictorId,
                                (HorseRacingPrediction.Contracts.Predictions.TicketStatus)ticket.Status,
                                ticket.ConfidenceScore, ticket.SummaryComment, ticket.PredictedAt,
                                ticket.Marks.Select(mark => new PredictionMarkSnapshotDto(mark.EntryId, mark.MarkCode,
                                    mark.PredictedRank, mark.Score, mark.Comment)).ToArray(),
                                ticket.LatestEvaluation is null ? null : new PredictionEvaluationDto(
                                    ticket.LatestEvaluation.EvaluatedAt, ticket.LatestEvaluation.EvaluationRevision,
                                    ticket.LatestEvaluation.HitTypeCodes, ticket.LatestEvaluation.ScoreSummary,
                                    ticket.LatestEvaluation.ReturnAmount, ticket.LatestEvaluation.Roi),
                                (HorseRacingPrediction.Contracts.Predictions.EvaluationStatus)ticket.EvaluationStatus)).ToArray(),
                            readModel.EntryResults.Select(result => new EntryResultSnapshotDto(result.EntryId,
                                result.HorseId, result.HorseNumber, result.FinishPosition, result.OfficialTime,
                                result.MarginText, result.LastThreeFurlongTime, result.AbnormalResultCode,
                                result.PrizeMoney, result.CornerPositions, result.Popularity,
                                result.OriginalFinishPosition, result.IsDeadHeat, result.Average1F,
                                result.AdditionalPrizeMoney)).ToArray(), ToDto(readModel.PayoutResult))));
                    })
                    .WithName("GetPredictionComparison")
                    .WithTags("Race API")
                    .Produces<GetPredictionComparisonResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }

    private static RacePayoutResultDto? ToDto(AppReadModels.PayoutResultSnapshot? payout) => payout is null ? null :
        new(payout.DeclaredAt, payout.WinPayouts.Select(ToDto).ToArray(), payout.PlacePayouts.Select(ToDto).ToArray(),
            payout.QuinellaPayouts.Select(ToDto).ToArray(), payout.ExactaPayouts.Select(ToDto).ToArray(),
            payout.TrifectaPayouts.Select(ToDto).ToArray(), payout.BracketQuinellaPayouts?.Select(ToDto).ToArray(),
            payout.WidePayouts?.Select(ToDto).ToArray(), payout.TrioPayouts?.Select(ToDto).ToArray());

    private static RacePayoutEntryDto ToDto(AppReadModels.PayoutEntrySnapshot payout) =>
        new(payout.Combination, payout.Amount);
}
