using EventFlow.EntityFramework;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

using static HorseRacingPrediction.Api.Endpoints.Shared.EndpointQueryUtilities;


internal static class SearchRacesEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/races",
                    [SwaggerOperation(Summary = "Search races", Description = "Returns paged race summaries filtered by date, course, status, race name and result information")]
        async ([AsParameters] SearchRacesRequest request,
                        IDbContextProvider<EventStoreDbContext> dbContextProvider,
                        CancellationToken cancellationToken) =>
                    {
                        var page = request.Page ?? 1;
                        var pageSize = request.PageSize ?? 20;
                        var sortBy = request.SortBy ?? "raceDate";
                        var sortDescending = request.SortDescending ?? true;

                        var pagingError = ValidatePaging(page, pageSize);
                        if (pagingError is not null)
                            return Results.BadRequest(new[] { pagingError });

                        using var dbContext = dbContextProvider.CreateContext();
                        var allRaces = await dbContext.Set<RaceSummaryReadModel>()
                            .AsNoTracking()
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        IEnumerable<RaceSummaryReadModel> filtered = allRaces;

                        if (!string.IsNullOrWhiteSpace(request.RaceId))
                            filtered = filtered.Where(x => string.Equals(x.RaceId, request.RaceId, StringComparison.OrdinalIgnoreCase));

                        if (request.RaceDateFrom.HasValue)
                            filtered = filtered.Where(x => x.RaceDate.HasValue && x.RaceDate.Value >= request.RaceDateFrom.Value);

                        if (request.RaceDateTo.HasValue)
                            filtered = filtered.Where(x => x.RaceDate.HasValue && x.RaceDate.Value <= request.RaceDateTo.Value);

                        if (!string.IsNullOrWhiteSpace(request.RaceCourseCode))
                            filtered = filtered.Where(x => string.Equals(x.RacecourseCode, request.RaceCourseCode, StringComparison.OrdinalIgnoreCase));

                        if (request.RaceNumber.HasValue)
                            filtered = filtered.Where(x => x.RaceNumber == request.RaceNumber.Value);

                        if (!string.IsNullOrWhiteSpace(request.RaceName))
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.RaceName, request.RaceName));

                        if (request.Status.HasValue)
                            filtered = filtered.Where(x => x.Status == (HorseRacingPrediction.Domain.Races.RaceStatus)request.Status.Value);

                        if (!string.IsNullOrWhiteSpace(request.WinningHorseName))
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.WinningHorseName, request.WinningHorseName));

                        filtered = sortBy.ToLowerInvariant() switch
                        {
                            "racedate" => sortDescending
                                ? filtered.OrderByDescending(x => x.RaceDate).ThenByDescending(x => x.RaceNumber)
                                : filtered.OrderBy(x => x.RaceDate).ThenBy(x => x.RaceNumber),
                            "racenumber" => sortDescending
                                ? filtered.OrderByDescending(x => x.RaceNumber).ThenByDescending(x => x.RaceDate)
                                : filtered.OrderBy(x => x.RaceNumber).ThenBy(x => x.RaceDate),
                            "racename" => sortDescending
                                ? filtered.OrderByDescending(x => x.RaceName)
                                : filtered.OrderBy(x => x.RaceName),
                            "status" => sortDescending
                                ? filtered.OrderByDescending(x => x.Status)
                                : filtered.OrderBy(x => x.Status),
                            "resultdeclaredat" => sortDescending
                                ? filtered.OrderByDescending(x => x.ResultDeclaredAt).ThenByDescending(x => x.RaceDate)
                                : filtered.OrderBy(x => x.ResultDeclaredAt).ThenBy(x => x.RaceDate),
                            _ => null!
                        };

                        if (filtered is null)
                        {
                            return Results.BadRequest(new[]
                            {
                                "SortBy must be one of: raceDate, raceNumber, raceName, status, resultDeclaredAt."
                            });
                        }

                        var totalCount = filtered.Count();
                        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
                        var items = filtered
                            .Skip((page - 1) * pageSize)
                            .Take(pageSize)
                            .Select(x => new RaceSummaryDto(
                                x.RaceId,
                                x.RaceDate,
                                x.RacecourseCode,
                                x.RaceNumber,
                                x.RaceName,
                                (HorseRacingPrediction.Contracts.Races.RaceStatus)(int)x.Status,
                                x.EntryCount,
                                x.WinningHorseName,
                                x.ResultDeclaredAt,
                                x.ReplacementRaceId))
                            .ToList();

                        return Results.Ok(new SearchRacesResponse(
                            items,
                            new PaginationDto(page, pageSize, totalCount, totalPages)));
                    })
                    .WithName("SearchRaces")
                    .WithTags("Race API")
                    .Produces<SearchRacesResponse>(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest);
    }
}
