using EventFlow.EntityFramework;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Contracts.Owners;

namespace HorseRacingPrediction.Api.Endpoints.Owners;

using static HorseRacingPrediction.Api.Endpoints.Owners.OwnerEndpointService;


internal static class MergeOwnerEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/owners/{ownerId}/merge",
                    async (string ownerId, MergeOwnerRequest request, HttpContext httpContext, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        if (string.IsNullOrWhiteSpace(request.SourceOwnerId) || string.IsNullOrWhiteSpace(request.Reason))
                            return Results.BadRequest(new[] { "統合元の馬主と理由は必須です。" });
                        if (string.Equals(ownerId, request.SourceOwnerId, StringComparison.Ordinal))
                            return Results.BadRequest(new[] { "同じ馬主には統合できません。" });

                        using var dbContext = dbContextProvider.CreateContext();
                        var owners = await BuildOwnersAsync(dbContext, cancellationToken).ConfigureAwait(false);
                        var target = owners.SingleOrDefault(x => x.OwnerId == ownerId);
                        var source = owners.SingleOrDefault(x => x.OwnerId == request.SourceOwnerId);
                        if (target is null || source is null) return Results.NotFound();

                        var actor = "Admin UI";
                        var now = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now();
                        foreach (var alias in source.NameVariants)
                        {
                            var normalized = NormalizeOwnerName(alias);
                            var mapping = await dbContext.OwnerAliasMappings.SingleOrDefaultAsync(x => x.NormalizedAlias == normalized, cancellationToken).ConfigureAwait(false);
                            if (mapping is null)
                            {
                                dbContext.OwnerAliasMappings.Add(new OwnerAliasMappingReadModel { NormalizedAlias = normalized, AliasName = alias, OwnerId = ownerId, ActorId = actor, Reason = request.Reason.Trim(), CreatedAt = now });
                            }
                            else
                            {
                                mapping.OwnerId = ownerId; mapping.AliasName = alias; mapping.ActorId = actor; mapping.Reason = request.Reason.Trim(); mapping.CreatedAt = now;
                            }
                        }
                        dbContext.OwnerMergeAudits.Add(new OwnerMergeAuditReadModel
                        {
                            AuditId = Guid.NewGuid().ToString("N"),
                            SourceOwnerId = source.OwnerId,
                            TargetOwnerId = ownerId,
                            SourceNames = string.Join('\n', source.NameVariants),
                            ActorId = actor,
                            Reason = request.Reason.Trim(),
                            CreatedAt = now
                        });
                        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        return Results.NoContent();
                    })
                    .WithName("MergeOwner")
                    .WithTags("Owner API")
                    .Produces(StatusCodes.Status204NoContent)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
