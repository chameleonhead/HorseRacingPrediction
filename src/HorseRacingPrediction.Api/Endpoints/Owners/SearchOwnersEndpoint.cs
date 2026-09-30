using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;

using HorseRacingPrediction.Contracts.Owners;

namespace HorseRacingPrediction.Api.Endpoints.Owners;

using static HorseRacingPrediction.Api.Endpoints.Owners.OwnerEndpointService;


internal static class SearchOwnersEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/owners",
                    async (string? query, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        using var dbContext = dbContextProvider.CreateContext();
                        var owners = await BuildOwnersAsync(dbContext, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(query))
                            owners = owners.Where(x => x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || x.NameVariants.Any(y => y.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
                        return Results.Ok(owners.OrderBy(x => x.DisplayName, StringComparer.Ordinal).ToList());
                    })
                    .WithName("SearchOwners")
                    .WithTags("Owner API")
                    .Produces<IReadOnlyList<OwnerSummaryDto>>();
    }
}
