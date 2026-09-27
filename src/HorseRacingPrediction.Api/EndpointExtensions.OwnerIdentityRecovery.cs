using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api;

public sealed record OwnerIdentityRecoveryCandidate(Guid NotificationId, Guid TaskId, string SourceId,
    string? Name, string? RaceId, string? TargetId, string? Fingerprint, string? BlockingReason);
public sealed record OwnerIdentityRecoverySelection(Guid NotificationId, string Fingerprint);
public sealed record OwnerIdentityRecoveryRequest(IReadOnlyList<OwnerIdentityRecoverySelection> Items);

public static partial class EndpointExtensions
{
    private const string OwnerIdentityRecoveryId = "owner-identity-contract-v1";

    private static void MapOwnerIdentityRecoveryEndpoints(RouteGroupBuilder group)
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

    private static string OwnerRecoveryKey(Guid notificationId) => $"{OwnerIdentityRecoveryId}:{notificationId:N}";

    private static async Task<OwnerIdentityRecoveryCandidate> PreviewOwnerIdentityRecoveryAsync(EventStoreDbContext db,
        CollectionPlatformStore store, PendingCollectionFailureNotification failure, CancellationToken token)
    {
        OwnerIdentityRecoveryCandidate Block(string reason) => new(failure.NotificationId, failure.TaskId,
            failure.Resource.Id, null, null, null, null, reason);
        if (failure.Resource.Type != CollectionResourceType.Owner || failure.Resource.Provider != "JRA"
            || failure.Definition.Value != "owner-identity" || failure.ErrorCode != SubjectNotIdentifiedErrorCode
            || failure.Status is not (CollectionTaskStatus.Failed or CollectionTaskStatus.DeadLetter))
            return Block("NotConfirmedOwnerIdentityFailure");
        var evidence = await store.GetTaskEvidenceAsync(failure.TaskId, token);
        var task = evidence?.Task;
        var name = task?.Metadata?.GetValueOrDefault("name");
        var raceId = task?.Metadata?.GetValueOrDefault("requestedByRaceId");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(raceId)) return Block("MissingOriginalRequestEvidence");
        // This operation repairs only the reproduced former bulk-job algorithm, not arbitrary missing IDs.
        var faultyId = DeterministicIdGenerator.BuildEntityId("owner", DeterministicIdGenerator.NormalizeKey(name));
        if (task!.Resource != failure.Resource || task.Definition != failure.Definition
            || faultyId != failure.Resource.Id)
            return Block("NotKnownFaultyOwnerId");
        var aliases = await db.OwnerAliasMappings.AsNoTracking().ToDictionaryAsync(x => x.NormalizedAlias, x => x.OwnerId, token);
        var target = OwnerIdentityContract.ResolveId(name, aliases)!;
        var race = await db.RacePredictionContexts.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == raceId, token);
        var owners = await BuildOwnersAsync(db, token);
        if (target == failure.Resource.Id || owners.Any(x => x.OwnerId == failure.Resource.Id)
            || !owners.Any(x => x.OwnerId == target)
            || race is null || !race.Entries.Any(x => OwnerIdentityContract.NormalizeName(x.OwnerName ?? "") == OwnerIdentityContract.NormalizeName(name)
                && OwnerIdentityContract.ResolveId(x.OwnerName, aliases) == target))
            return Block("OwnerOrRaceEvidenceNotUnique");
        var batches = await store.GetBatchResourceStatusesAsync(OwnerRecoveryKey(failure.NotificationId), token);
        if (failure.ResolutionStatus != CollectionFailureResolutionStatus.Open
            && !(failure.ResolutionStatus == CollectionFailureResolutionStatus.Superseded && batches.Count == 1 && batches[0].Resource.Id == target))
            return Block("FailureAlreadyHandledByAnotherOperation");
        var snapshot = new
        {
            failure.NotificationId,
            failure.TaskId,
            failure.Resource,
            name,
            raceId,
            target,
            OriginalEvidence = evidence,
            failure.ErrorCode,
            failure.AttemptCount,
            failure.FailedAt,
            Revision = CollectionDefinitionRevisions.OwnerIdentity,
            Entries = race.Entries.OrderBy(x => x.EntryId).Select(x => new { x.EntryId, x.OwnerName, OwnerId = OwnerIdentityContract.ResolveId(x.OwnerName, aliases) })
        };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot))));
        return new(failure.NotificationId, failure.TaskId, failure.Resource.Id, name, raceId, target, fingerprint, null);
    }
}
