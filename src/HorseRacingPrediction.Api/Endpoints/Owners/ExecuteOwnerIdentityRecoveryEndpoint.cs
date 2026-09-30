using EventFlow.EntityFramework;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Owners;

using static HorseRacingPrediction.Api.OwnerIdentityRecoveryService;
using HorseRacingPrediction.Api;


internal static class ExecuteOwnerIdentityRecoveryEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/admin/repairs/owner-identity/execute", async (OwnerIdentityRecoveryRequest request,
                    IDbContextProvider<EventStoreDbContext> provider, CollectionPlatformStore store, CancellationToken token) =>
                {
                    if (request.Items is null || request.Items.Count is < 1 or > 1000
                        || request.Items.Select(x => x.NotificationId).Distinct().Count() != request.Items.Count)
                        return Results.BadRequest(new { code = "InvalidRecoverySelection" });
                    if (!(await store.GetPipelineStateAsync(token)).IsPaused
                        || (await store.GetTasksAsync(CollectionTaskStatus.Running, 1, token)).Count != 0)
                        return Results.Conflict(new { code = "PausedAndDrainedPipelineRequired" });
                    var failures = await store.GetFailureNotificationsAsync(request.Items.Select(x => x.NotificationId).ToArray(), token);
                    if (failures.Count != request.Items.Count) return Results.Conflict(new { code = "RecoverySelectionChanged" });
                    using var db = provider.CreateContext();
                    var plans = new List<(PendingCollectionFailureNotification Failure, OwnerIdentityRecoveryCandidate Candidate)>();
                    foreach (var failure in failures)
                    {
                        var candidate = await PreviewOwnerIdentityRecoveryAsync(db, store, failure, token);
                        if (candidate.BlockingReason is not null || candidate.Fingerprint != request.Items.Single(x => x.NotificationId == failure.NotificationId).Fingerprint)
                            return Results.Conflict(new { code = "RecoveryEvidenceChanged", candidate });
                        plans.Add((failure, candidate));
                    }
                    var receipts = new List<CollectionRequestReceipt>();
                    foreach (var (failure, candidate) in plans)
                    {
                        // Durable idempotency key is recorded before suppression; a partial apply can be replayed.
                        var receipt = await store.RequestAsync(new(CollectionResourceType.Owner, "JRA", candidate.TargetId!), new("owner-identity"),
                            CollectionDefinitionRevisions.OwnerIdentity, CollectionReason.Recovery, DateTimeOffset.UtcNow,
                            CollectionLane.Normal, (int)CollectionPriority.High, batchId: OwnerRecoveryKey(failure.NotificationId),
                            attributes: new Dictionary<string, string> { ["name"] = candidate.Name!, ["requestedByRaceId"] = candidate.RaceId! },
                            cancellationToken: token, payloadFingerprint: candidate.Fingerprint);
                        if (receipt.TaskId is null) return Results.Conflict(new { code = "RecoveryTargetUnavailable" });
                        await store.SuppressResourceAsync(failure.Resource, "Confirmed owner identity mismatch; replacement request retains original failure history.",
                            OwnerIdentityRecoveryId, DateTimeOffset.UtcNow, token);
                        receipts.Add(receipt);
                    }
                    return Results.Ok(receipts);
                });
    }
}
