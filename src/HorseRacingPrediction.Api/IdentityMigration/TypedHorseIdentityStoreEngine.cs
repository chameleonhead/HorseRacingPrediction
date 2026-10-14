using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum HorseIdentityStorePlanStatus
{
    InvalidManifest,
    Incomplete,
    DestinationCollision,
}

internal enum HorseIdentityStorePlanReason
{
    InvalidManifest,
    StoreUnavailable,
    StoreSchemaIncomplete,
    SourceRootMissing,
    DestinationCollision,
    TypedReferenceClosurePending,
    ReadUnavailable,
}

internal sealed record HorseIdentityStorePlan(
    HorseIdentityStorePlanStatus Status,
    HorseIdentityStorePlanReason Reason,
    IReadOnlyList<MigrationStoreSchema> Stores,
    IReadOnlyList<MigrationRootProbe> Roots,
    int ManifestErrorCount,
    bool CanWrite,
    IReadOnlyList<TypedHorseIdentityPageEvidence> TypedReferenceEvidence,
    string? TypedReferenceFailure)
{
    internal static HorseIdentityStorePlan Invalid(int errorCount) => new(
        HorseIdentityStorePlanStatus.InvalidManifest,
        HorseIdentityStorePlanReason.InvalidManifest,
        [], [], errorCount, CanWrite: false, [], null);
}

/// <summary>
/// T7a schema/root-only planner. It always stops before any physical write until later typed-reference
/// coverage and writer/journal slices are implemented and separately released.
/// </summary>
internal sealed class TypedHorseIdentityStoreEngine(TypedHorseIdentityStoreReaders readers)
{
    internal async Task<HorseIdentityStorePlan> InspectAsync(
        HorseIdentityMigrationManifest? manifest,
        CancellationToken cancellationToken)
    {
        var validation = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        if (!validation.IsValid)
            return HorseIdentityStorePlan.Invalid(validation.Errors.Count);

        try
        {
            var snapshot = await readers.InspectAsync(validation.Mappings, cancellationToken).ConfigureAwait(false);
            if (snapshot.Schemas.Any(schema => schema.Status is MigrationStoreReadStatus.Unavailable))
                return Incomplete(HorseIdentityStorePlanReason.StoreUnavailable, snapshot, validation.Errors.Count);
            if (snapshot.Schemas.Any(schema => schema.Status is not (MigrationStoreReadStatus.Observed
                    or MigrationStoreReadStatus.NotConfigured)))
                return Incomplete(HorseIdentityStorePlanReason.StoreSchemaIncomplete, snapshot, validation.Errors.Count);

            if (snapshot.Roots.Any(root => root.HasTargetCollision))
                return new(HorseIdentityStorePlanStatus.DestinationCollision,
                    HorseIdentityStorePlanReason.DestinationCollision, snapshot.Schemas, snapshot.Roots,
                    validation.Errors.Count, CanWrite: false, snapshot.TypedReferences?.Pages ?? [],
                    snapshot.TypedReferences?.Failure);
            if (snapshot.Roots.Any(root => root.SourceStatus != MigrationRootStatus.Present))
                return Incomplete(HorseIdentityStorePlanReason.SourceRootMissing, snapshot, validation.Errors.Count);

            if (snapshot.TypedReferences?.HasCollision == true)
                return new(HorseIdentityStorePlanStatus.DestinationCollision,
                    HorseIdentityStorePlanReason.DestinationCollision, snapshot.Schemas, snapshot.Roots,
                    validation.Errors.Count, CanWrite: false, snapshot.TypedReferences.Pages,
                    snapshot.TypedReferences.Failure);

            // The typed closure is read-only evidence only; the physical writer remains a later release.
            return Incomplete(HorseIdentityStorePlanReason.TypedReferenceClosurePending, snapshot,
                validation.Errors.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException)
        {
            // Do not echo file paths, SQL, source values, or provider exception details.
            return new(HorseIdentityStorePlanStatus.Incomplete, HorseIdentityStorePlanReason.ReadUnavailable,
                [], [], validation.Errors.Count, CanWrite: false, [], null);
        }
    }

    private static HorseIdentityStorePlan Incomplete(
        HorseIdentityStorePlanReason reason, MigrationStoreReadSnapshot snapshot, int manifestErrorCount) => new(
        HorseIdentityStorePlanStatus.Incomplete,
        reason,
        snapshot.Schemas,
        snapshot.Roots,
        ManifestErrorCount: manifestErrorCount,
        CanWrite: false,
        TypedReferenceEvidence: snapshot.TypedReferences?.Pages ?? [],
        TypedReferenceFailure: snapshot.TypedReferences?.Failure);
}
