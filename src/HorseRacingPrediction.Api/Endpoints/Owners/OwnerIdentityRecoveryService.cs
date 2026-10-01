using static HorseRacingPrediction.Api.Endpoints.Owners.OwnerEndpointService;
using static HorseRacingPrediction.Api.Endpoints.Repairs.SubjectIdentificationRepairService;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Owners;

namespace HorseRacingPrediction.Api;

public sealed record OwnerIdentityRecoveryCandidate(Guid NotificationId, Guid TaskId, string SourceId,
    string? Name, string? RaceId, string? TargetId, string? Fingerprint, string? BlockingReason);
internal static class OwnerIdentityRecoveryService
{
    internal const string OwnerIdentityRecoveryId = "owner-identity-contract-v1";


    internal static string OwnerRecoveryKey(Guid notificationId) => $"{OwnerIdentityRecoveryId}:{notificationId:N}";

    internal static async Task<OwnerIdentityRecoveryCandidate> PreviewOwnerIdentityRecoveryAsync(EventStoreDbContext db,
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
