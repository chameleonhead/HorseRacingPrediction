using EventFlow.EntityFramework;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

using static HorseRacingPrediction.Api.Endpoints.Shared.EndpointQueryUtilities;
using static HorseRacingPrediction.Api.Endpoints.Predictions.PredictionEndpointMappings;


internal static class SearchPredictionTicketsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/predictions",
                    [SwaggerOperation(Summary = "Search prediction tickets", Description = "Returns paged prediction ticket summaries filtered by race, predictor, ticket status, evaluation status and confidence score")]
        async ([AsParameters] SearchPredictionTicketsRequest request,
                        IDbContextProvider<EventStoreDbContext> dbContextProvider,
                        CancellationToken cancellationToken) =>
                    {
                        var page = request.Page ?? 1;
                        var pageSize = request.PageSize ?? 20;
                        var pagingError = ValidatePaging(page, pageSize);
                        if (pagingError is not null)
                            return Results.BadRequest(new[] { pagingError });

                        using var dbContext = dbContextProvider.CreateContext();
                        var allTickets = await dbContext.Set<PredictionTicketReadModel>()
                            .AsNoTracking()
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var races = await dbContext.RacePredictionContexts.AsNoTracking().ToDictionaryAsync(x => x.RaceId, cancellationToken).ConfigureAwait(false);
                        var horseNames = await dbContext.Horses.AsNoTracking().ToDictionaryAsync(x => x.HorseId, x => x.RegisteredName, cancellationToken).ConfigureAwait(false);

                        IEnumerable<PredictionTicketReadModel> filtered = allTickets;

                        if (!string.IsNullOrWhiteSpace(request.Query))
                        {
                            var term = request.Query.Trim();
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.RaceId, term)
                                || (x.RaceId is not null && races.TryGetValue(x.RaceId, out var race)
                                    && (ContainsIgnoreCase(race.RaceName, term)
                                        || ContainsIgnoreCase(race.RacecourseCode, term)
                                        || (race.RaceDate?.ToString("yyyy/MM/dd").Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                                        || (race.RaceDate?.ToString("yyyy-MM-dd").Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))));
                        }

                        if (!string.IsNullOrWhiteSpace(request.PredictionTicketId))
                            filtered = filtered.Where(x => string.Equals(x.PredictionTicketId, request.PredictionTicketId, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(request.RaceId))
                            filtered = filtered.Where(x => string.Equals(x.RaceId, request.RaceId, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(request.PredictorType))
                            filtered = filtered.Where(x => string.Equals(x.PredictorType, request.PredictorType, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(request.PredictorId))
                            filtered = filtered.Where(x => string.Equals(x.PredictorId, request.PredictorId, StringComparison.OrdinalIgnoreCase));

                        if (request.TicketStatus.HasValue)
                            filtered = filtered.Where(x => x.TicketStatus == (HorseRacingPrediction.Domain.Predictions.TicketStatus)request.TicketStatus.Value);

                        if (request.EvaluationStatus.HasValue)
                            filtered = filtered.Where(x => x.EvaluationStatus == (HorseRacingPrediction.Application.Queries.ReadModels.EvaluationStatus)request.EvaluationStatus.Value);

                        if (request.PredictedAtFrom.HasValue)
                            filtered = filtered.Where(x => x.PredictedAt.HasValue && x.PredictedAt.Value >= request.PredictedAtFrom.Value);

                        if (request.PredictedAtTo.HasValue)
                            filtered = filtered.Where(x => x.PredictedAt.HasValue && x.PredictedAt.Value <= request.PredictedAtTo.Value);

                        if (request.MinConfidenceScore.HasValue)
                            filtered = filtered.Where(x => x.ConfidenceScore >= request.MinConfidenceScore.Value);

                        if (request.MaxConfidenceScore.HasValue)
                            filtered = filtered.Where(x => x.ConfidenceScore <= request.MaxConfidenceScore.Value);

                        if (!string.IsNullOrWhiteSpace(request.SummaryComment))
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.SummaryComment, request.SummaryComment));

                        var sorted = SortPredictionTickets(filtered, request);
                        if (sorted is null)
                        {
                            return Results.BadRequest(new[]
                            {
                                "SortBy must be one of: predictedAt, confidenceScore, ticketStatus, evaluationStatus."
                            });
                        }

                        return Results.Ok(ToPagedResponse(
                            sorted,
                            page,
                            pageSize,
                            x =>
                            {
                                races.TryGetValue(x.RaceId ?? string.Empty, out var race);
                                var primaryMark = x.Marks.OrderBy(m => m.PredictedRank).FirstOrDefault();
                                var primaryHorseId = primaryMark is not null && race is not null
                                    ? race.Entries.FirstOrDefault(e => e.EntryId == primaryMark.EntryId)?.HorseId
                                    : null;
                                return new PredictionTicketSummaryDto(
                                x.PredictionTicketId,
                                x.RaceId,
                                x.PredictorType,
                                x.PredictorId,
                                x.ConfidenceScore,
                                x.SummaryComment,
                                x.PredictedAt,
                                (HorseRacingPrediction.Contracts.Predictions.TicketStatus)(int)x.TicketStatus,
                                (HorseRacingPrediction.Contracts.Predictions.EvaluationStatus)(int)x.EvaluationStatus,
                                x.Marks.Count,
                                race?.RaceName, race?.RaceDate, race?.RacecourseCode, race?.RaceNumber,
                                primaryHorseId is null ? null : horseNames.GetValueOrDefault(primaryHorseId));
                            }));
                    })
                    .WithName("SearchPredictionTickets")
                    .WithTags("Prediction API")
                    .Produces<PagedResponse<PredictionTicketSummaryDto>>(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest);
    }
}
