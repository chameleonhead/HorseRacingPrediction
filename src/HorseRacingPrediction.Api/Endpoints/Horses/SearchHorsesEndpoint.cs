using EventFlow.EntityFramework;
using HorseRacingPrediction.Contracts;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

using static HorseRacingPrediction.Api.Endpoints.Shared.EndpointQueryUtilities;
using static HorseRacingPrediction.Api.Endpoints.Horses.HorseEndpointMappings;


internal static class SearchHorsesEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/horses",
                    [SwaggerOperation(Summary = "Search horses", Description = "Returns paged horse summaries filtered by identifiers, names, sex, birth date and aliases")]
        async ([AsParameters] SearchHorsesRequest request,
                        IDbContextProvider<EventStoreDbContext> dbContextProvider,
                        CancellationToken cancellationToken) =>
                    {
                        var page = request.Page ?? 1;
                        var pageSize = request.PageSize ?? 20;
                        var pagingError = ValidatePaging(page, pageSize);
                        if (pagingError is not null)
                            return Results.BadRequest(new[] { pagingError });

                        using var dbContext = dbContextProvider.CreateContext();
                        var allHorses = await dbContext.Set<AppReadModels.HorseReadModel>()
                            .AsNoTracking()
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var redirectedIds = (await dbContext.HorseIdentityRepairRedirects.AsNoTracking()
                            .Select(x => x.SourceHorseId).ToListAsync(cancellationToken).ConfigureAwait(false))
                            .ToHashSet(StringComparer.Ordinal);

                        IEnumerable<AppReadModels.HorseReadModel> filtered = allHorses.Where(x => !redirectedIds.Contains(x.HorseId));

                        if (!string.IsNullOrWhiteSpace(request.HorseId))
                            filtered = filtered.Where(x => string.Equals(x.HorseId, request.HorseId, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(request.Query))
                        {
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.RegisteredName, request.Query)
                                || ContainsIgnoreCase(x.NormalizedName, request.Query)
                                || x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.Query)));
                        }

                        if (!string.IsNullOrWhiteSpace(request.RegisteredName))
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.RegisteredName, request.RegisteredName));

                        if (!string.IsNullOrWhiteSpace(request.NormalizedName))
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.NormalizedName, request.NormalizedName));

                        if (!string.IsNullOrWhiteSpace(request.SexCode))
                            filtered = filtered.Where(x => string.Equals(x.SexCode, request.SexCode, StringComparison.OrdinalIgnoreCase));

                        if (request.BirthDateFrom.HasValue)
                            filtered = filtered.Where(x => x.BirthDate.HasValue && x.BirthDate.Value >= request.BirthDateFrom.Value);

                        if (request.BirthDateTo.HasValue)
                            filtered = filtered.Where(x => x.BirthDate.HasValue && x.BirthDate.Value <= request.BirthDateTo.Value);

                        if (!string.IsNullOrWhiteSpace(request.AliasValue))
                            filtered = filtered.Where(x => x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.AliasValue)));

                        var sorted = SortHorses(filtered, request);
                        if (sorted is null)
                        {
                            return Results.BadRequest(new[]
                            {
                                "SortBy must be one of: registeredName, normalizedName, birthDate."
                            });
                        }

                        return Results.Ok(ToPagedResponse(
                            sorted,
                            page,
                            pageSize,
                            x => new HorseSummaryResponse(
                                x.HorseId,
                                x.RegisteredName,
                                x.NormalizedName,
                                x.SexCode,
                                x.BirthDate,
                                x.Aliases.Count)));
                    })
                    .WithName("SearchHorses")
                    .WithTags("Horse API")
                    .Produces<PagedResponse<HorseSummaryResponse>>(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest);
    }
}
