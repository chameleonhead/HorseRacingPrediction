using EventFlow.EntityFramework;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Contracts.Owners;

namespace HorseRacingPrediction.Api.Endpoints.Owners;

using static HorseRacingPrediction.Api.Endpoints.Owners.OwnerEndpointService;


internal static class UpdateOwnerEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/owners/{ownerId}",
                    async (string ownerId, UpdateOwnerRequest request, HttpContext httpContext, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Reason))
                            return Results.BadRequest(new[] { "表示名と訂正理由は必須です。" });
                        using var dbContext = dbContextProvider.CreateContext();
                        var owners = await BuildOwnersAsync(dbContext, cancellationToken).ConfigureAwait(false);
                        if (owners.All(x => x.OwnerId != ownerId)) return Results.NotFound();
                        var now = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now();
                        var normalized = NormalizeOwnerName(request.DisplayName.Trim());
                        if (normalized.Length == 0) return Results.BadRequest(new[] { "表示名を入力してください。" });
                        var mapping = await dbContext.OwnerAliasMappings.SingleOrDefaultAsync(x => x.NormalizedAlias == normalized, cancellationToken).ConfigureAwait(false);
                        if (mapping is null)
                        {
                            mapping = new OwnerAliasMappingReadModel { NormalizedAlias = normalized };
                            dbContext.OwnerAliasMappings.Add(mapping);
                        }
                        mapping.AliasName = request.DisplayName.Trim(); mapping.OwnerId = ownerId; mapping.ActorId = "Admin UI"; mapping.Reason = request.Reason.Trim(); mapping.CreatedAt = now; mapping.IsDisplayName = true;
                        foreach (var alias in (request.NameVariants ?? []).Append(request.DisplayName).Select(x => x?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
                        {
                            var aliasNormalized = NormalizeOwnerName(alias!);
                            if (aliasNormalized == normalized) continue;
                            var aliasMapping = await dbContext.OwnerAliasMappings.SingleOrDefaultAsync(x => x.NormalizedAlias == aliasNormalized, cancellationToken).ConfigureAwait(false);
                            if (aliasMapping is null)
                            {
                                dbContext.OwnerAliasMappings.Add(new OwnerAliasMappingReadModel { NormalizedAlias = aliasNormalized, AliasName = alias!, OwnerId = ownerId, ActorId = "Admin UI", Reason = request.Reason.Trim(), CreatedAt = now, IsDisplayName = aliasNormalized == normalized });
                            }
                            else
                            {
                                aliasMapping.AliasName = alias!; aliasMapping.OwnerId = ownerId; aliasMapping.ActorId = "Admin UI"; aliasMapping.Reason = request.Reason.Trim(); aliasMapping.CreatedAt = now; aliasMapping.IsDisplayName = aliasNormalized == normalized;
                            }
                        }
                        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        return Results.NoContent();
                    })
                    .WithName("UpdateOwner")
                    .WithTags("Owner API")
                    .Produces(StatusCodes.Status204NoContent)
                    .ProducesValidationProblem();
    }
}
