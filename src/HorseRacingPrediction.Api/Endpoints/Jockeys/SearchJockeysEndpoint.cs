using EventFlow.EntityFramework;
using HorseRacingPrediction.Contracts;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

using static HorseRacingPrediction.Api.Endpoints.Shared.EndpointQueryUtilities;
using static HorseRacingPrediction.Api.Endpoints.Jockeys.JockeyEndpointMappings;


internal static class SearchJockeysEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/jockeys",
                    [SwaggerOperation(Summary = "Search jockeys", Description = "Returns paged jockey summaries filtered by identifiers, names, affiliation and aliases")]
        async ([AsParameters] SearchJockeysRequest request,
                        IDbContextProvider<EventStoreDbContext> dbContextProvider,
                        CancellationToken cancellationToken) =>
                    {
                        var page = request.Page ?? 1;
                        var pageSize = request.PageSize ?? 20;
                        var pagingError = ValidatePaging(page, pageSize);
                        if (pagingError is not null)
                            return Results.BadRequest(new[] { pagingError });

                        using var dbContext = dbContextProvider.CreateContext();
                        var allJockeys = await dbContext.Set<AppReadModels.JockeyReadModel>()
                            .AsNoTracking()
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        IEnumerable<AppReadModels.JockeyReadModel> filtered = allJockeys;

                        if (!string.IsNullOrWhiteSpace(request.JockeyId))
                            filtered = filtered.Where(x => string.Equals(x.JockeyId, request.JockeyId, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(request.Query))
                        {
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.DisplayName, request.Query)
                                || ContainsIgnoreCase(x.NormalizedName, request.Query)
                                || x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.Query)));
                        }

                        if (!string.IsNullOrWhiteSpace(request.DisplayName))
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.DisplayName, request.DisplayName));

                        if (!string.IsNullOrWhiteSpace(request.NormalizedName))
                            filtered = filtered.Where(x => ContainsIgnoreCase(x.NormalizedName, request.NormalizedName));

                        if (!string.IsNullOrWhiteSpace(request.AffiliationCode))
                            filtered = filtered.Where(x => string.Equals(x.AffiliationCode, request.AffiliationCode, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(request.AliasValue))
                            filtered = filtered.Where(x => x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.AliasValue)));

                        var sorted = SortJockeys(filtered, request);
                        if (sorted is null)
                        {
                            return Results.BadRequest(new[]
                            {
                                "SortBy must be one of: displayName, normalizedName, affiliationCode."
                            });
                        }

                        return Results.Ok(ToPagedResponse(
                            sorted,
                            page,
                            pageSize,
                            x => new JockeySummaryResponse(
                                x.JockeyId,
                                x.DisplayName,
                                x.NormalizedName,
                                x.AffiliationCode,
                                x.Aliases.Count)));
                    })
                    .WithName("SearchJockeys")
                    .WithTags("Jockey API")
                    .Produces<PagedResponse<JockeySummaryResponse>>(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest);
    }
}
