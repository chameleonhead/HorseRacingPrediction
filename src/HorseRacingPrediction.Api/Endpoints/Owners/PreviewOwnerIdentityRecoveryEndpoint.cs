using EventFlow.EntityFramework;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Owners;

using static HorseRacingPrediction.Api.OwnerIdentityRecoveryService;
using HorseRacingPrediction.Api;


internal static class PreviewOwnerIdentityRecoveryEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/admin/repairs/owner-identity", async (IDbContextProvider<EventStoreDbContext> provider,
                    CollectionPlatformStore store, CancellationToken token) =>
                {
                    using var db = provider.CreateContext();
                    var failures = await store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow, int.MaxValue, token);
                    var result = new List<OwnerIdentityRecoveryCandidate>();
                    foreach (var failure in failures.Where(x => x.Resource.Type == CollectionResourceType.Owner))
                        result.Add(await PreviewOwnerIdentityRecoveryAsync(db, store, failure, token));
                    return Results.Ok(result);
                });
    }
}
