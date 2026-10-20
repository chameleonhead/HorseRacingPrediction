using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;

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
    internal Task<HorseIdentityMigrationSchemaDiagnostics> InspectSchemaDiagnosticsAsync(
        CancellationToken cancellationToken) => readers.InspectSchemaDiagnosticsAsync(cancellationToken);

    internal static HorseIdentityMigrationSchemaDiagnostics CreateIncompleteSchemaDiagnostics() =>
        new(DateTimeOffset.UtcNow, false, []);

    internal static HorseIdentityMigrationPreview CreateIncompletePreview() =>
        new(false, null, [HorseIdentityMigrationPreviewBlocker.IncompleteEvidence], [], [], [], [], 0, 0)
        {
            SchemaDiagnostics = CreateIncompleteSchemaDiagnostics(),
        };

    internal Task<MigrationStoreReadSnapshot> InspectExecutionInputsAsync(
        HorseIdentityMigrationManifest? manifest, CancellationToken cancellationToken)
    {
        var validation = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        if (!validation.IsValid)
            throw new InvalidOperationException("Migration manifest is invalid.");
        return readers.InspectAsync(validation.Mappings, cancellationToken);
    }

    internal async Task<HorseIdentityMigrationPreview> InspectPreviewAsync(
        HorseIdentityMigrationManifest? manifest, CancellationToken cancellationToken)
    {
        var validation = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        if (!validation.IsValid)
            return new(false, null, [HorseIdentityMigrationPreviewBlocker.InvalidManifest], [], [], [], [], 0, 0);

        MigrationStoreReadSnapshot snapshot;
        try
        {
            snapshot = await readers.InspectAsync(validation.Mappings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException or IOException)
        {
            return new(false, null, [HorseIdentityMigrationPreviewBlocker.ReadUnavailable], [], [], [], [], 0, 0);
        }

        var blockers = new List<HorseIdentityMigrationPreviewBlocker>();
        var executionBlocker = TypedHorseIdentityStoreWriter.FindPreflightBlocker(snapshot);
        if (executionBlocker is not null)
        {
            blockers.Add(executionBlocker.Value switch
            {
                HorseIdentityMigrationJournalReason.RequestConflict => HorseIdentityMigrationPreviewBlocker.DestinationCollision,
                HorseIdentityMigrationJournalReason.OperationInterrupted => HorseIdentityMigrationPreviewBlocker.StoreUnavailable,
                _ => HorseIdentityMigrationPreviewBlocker.IncompleteEvidence,
            });
        }

        var stores = snapshot.Schemas.OrderBy(item => item.Surface)
            .Select(item => new HorseIdentityMigrationPreviewStore(item.Surface.ToString(),
                item.Status.ToString(), item.TableCount, item.UnknownObjectCount, item.Fingerprint)).ToArray();
        var roots = snapshot.Roots.OrderBy(item => item.OldHorseId, StringComparer.Ordinal)
            .Select(item => new HorseIdentityMigrationPreviewRoot(item.OldHorseId, item.DestinationHorseId,
                item.SourceStatus.ToString(), item.HasTargetCollision)).ToArray();
        var typed = snapshot.TypedReferences;
        var pages = typed?.Pages
            .Where(item => !string.Equals(item.BeforeHash, item.ExpectedAfterHash, StringComparison.Ordinal))
            .OrderBy(item => item.Surface).ThenBy(item => item.Table, StringComparer.Ordinal)
            .ThenBy(item => item.FirstKey, StringComparer.Ordinal).ThenBy(item => item.LastKey, StringComparer.Ordinal)
            .Select(item => new HorseIdentityMigrationPreviewPage(item.Surface.ToString(), item.Table,
                item.RowCount, item.BeforeHash, item.ExpectedAfterHash)).ToArray() ?? [];
        var mappings = validation.Mappings.OrderBy(item => item.OldHorseId, StringComparer.Ordinal)
            .Select(item => new HorseIdentityMigrationPreviewMapping(item.OldHorseId,
                item.JraIdentity, item.DestinationHorseId)).ToArray();
        var fingerprint = BuildPreviewFingerprint(validation.Mappings, snapshot);
        var canStage = blockers.Count == 0 && fingerprint is not null;
        var preview = new HorseIdentityMigrationPreview(canStage, fingerprint,
            blockers.ToArray(), mappings, stores, roots, pages,
            typed?.InspectedRows ?? 0, typed?.InspectedBytes ?? 0)
        {
            SchemaDiagnostics = TypedHorseIdentityStoreReaders.ToSchemaDiagnostics(snapshot.Schemas),
        };
        return BoundPreviewSummary(preview);
    }

    internal static HorseIdentityMigrationPreview BoundPreviewSummary(HorseIdentityMigrationPreview preview)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(preview, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length
            <= HorseIdentityMigrationPreview.MaximumSummaryBytes)
            return preview;
        var bounded = new HorseIdentityMigrationPreview(false, null,
            [HorseIdentityMigrationPreviewBlocker.SummaryTooLarge], preview.Mappings, [], [], [], 0, 0)
        {
            SchemaDiagnostics = preview.SchemaDiagnostics is null
                ? null
                : TypedHorseIdentityStoreReaders.IncompleteSchemaDiagnostics(preview.SchemaDiagnostics),
        };
        return JsonSerializer.SerializeToUtf8Bytes(bounded,
                   new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length
               <= HorseIdentityMigrationPreview.MaximumSummaryBytes
            ? bounded
            : new(false, null, [HorseIdentityMigrationPreviewBlocker.SummaryTooLarge], [], [], [], [], 0, 0);
    }

    internal static string BuildPreviewFingerprint(
        IReadOnlyList<HorseIdentityMigrationMapping> mappings, MigrationStoreReadSnapshot snapshot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "horse-identity-preview-v1");
        var orderedMappings = mappings.OrderBy(item => item.OldHorseId, StringComparer.Ordinal)
            .ThenBy(item => item.JraIdentity, StringComparer.Ordinal)
            .ThenBy(item => item.DestinationHorseId, StringComparer.Ordinal).ToArray();
        AppendInt32(hash, orderedMappings.Length);
        foreach (var mapping in orderedMappings)
        {
            AppendString(hash, mapping.OldHorseId);
            AppendString(hash, mapping.JraIdentity);
            AppendString(hash, mapping.DestinationHorseId);
        }

        var schemas = snapshot.Schemas.OrderBy(item => item.Surface).ToArray();
        AppendInt32(hash, schemas.Length);
        foreach (var schema in schemas)
        {
            AppendInt32(hash, (int)schema.Surface);
            AppendInt32(hash, (int)schema.Status);
            AppendInt32(hash, schema.TableCount);
            AppendInt32(hash, schema.UnknownObjectCount);
            AppendNullableString(hash, schema.Fingerprint);
        }

        var roots = snapshot.Roots.OrderBy(item => item.OldHorseId, StringComparer.Ordinal)
            .ThenBy(item => item.DestinationHorseId, StringComparer.Ordinal).ToArray();
        AppendInt32(hash, roots.Length);
        foreach (var root in roots)
        {
            AppendString(hash, root.OldHorseId);
            AppendString(hash, root.DestinationHorseId);
            AppendInt32(hash, (int)root.SourceStatus);
            AppendBoolean(hash, root.TargetHorseRootExists);
            AppendBoolean(hash, root.TargetEventStreamExists);
            AppendBoolean(hash, root.TargetSnapshotExists);
            AppendBoolean(hash, root.TargetCollectionResourceExists);
            AppendBoolean(hash, root.TargetSuppressionExists);
            AppendBoolean(hash, root.TargetSelectionExists);
            AppendBoolean(hash, root.TargetFailureNotificationExists);
            AppendBoolean(hash, root.CollectionResourceNaturalKeyCollision);
            AppendBoolean(hash, root.SuppressionNaturalKeyCollision);
        }

        AppendBoolean(hash, snapshot.TypedReferenceClosureInspected);
        var typed = snapshot.TypedReferences;
        AppendBoolean(hash, typed is not null);
        if (typed is not null)
        {
            AppendInt32(hash, (int)typed.Status);
            AppendInt64(hash, typed.InspectedRows);
            AppendInt64(hash, typed.InspectedBytes);
            var pages = typed.Pages.OrderBy(item => item.Surface).ThenBy(item => item.Table, StringComparer.Ordinal)
                .ThenBy(item => item.FirstKey, StringComparer.Ordinal).ThenBy(item => item.LastKey, StringComparer.Ordinal)
                .ThenBy(item => item.RowCount).ThenBy(item => item.BeforeHash, StringComparer.Ordinal)
                .ThenBy(item => item.ExpectedAfterHash, StringComparer.Ordinal).ToArray();
            AppendInt32(hash, pages.Length);
            foreach (var page in pages)
            {
                AppendInt32(hash, (int)page.Surface);
                AppendString(hash, page.Table);
                AppendInt64(hash, page.RowCount);
                AppendNullableString(hash, page.FirstKey);
                AppendNullableString(hash, page.LastKey);
                AppendString(hash, page.BeforeHash);
                AppendString(hash, page.ExpectedAfterHash);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendNullableString(IncrementalHash hash, string? value)
    {
        AppendBoolean(hash, value is not null);
        if (value is not null) AppendString(hash, value);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendBoolean(IncrementalHash hash, bool value) =>
        hash.AppendData([value ? (byte)1 : (byte)0]);

    internal Task<TypedHorseIdentityExecutionResult> ExecuteOwnedAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeFirstWrite = null,
        Func<CancellationToken, Task>? afterPageCommit = null,
        TimeProvider? timeProvider = null) =>
        new TypedHorseIdentityStoreWriter(readers, this, journal, beforeFirstWrite, afterPageCommit, timeProvider)
            .ExecuteAsync(operationId, manifest, cancellationToken);

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
