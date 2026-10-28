using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum HorseIdentityMigrationOperationStatus
{
    Staged,
    Running,
    Succeeded,
    Rejected,
    Failed,
    Interrupted,
    PartialUnverified,
}

internal enum HorseIdentityMigrationJournalReason
{
    None,
    CapacityExceeded,
    OwnershipBusy,
    RequestConflict,
    StaleOperation,
    UnverifiedPages,
    OperationInterrupted,
    PreparationFailed,
    BackupFailed,
    InspectionFailed,
    ApplyFailed,
    VerificationFailed,
    ChildFailed,
    StartupFailed,
    PreparationCancelled,
}

internal enum HorseIdentityMigrationPageState
{
    Prepared,
    CommittedUnverified,
    Verified,
}

internal enum HorseIdentityMigrationResultCode
{
    PlanIncomplete,
    DestinationCollision,
    PageMismatch,
    CapacityExceeded,
    OperationInterrupted,
    PreparationFailed,
    BackupFailed,
    InspectionFailed,
    ApplyFailed,
    VerificationFailed,
    ChildFailed,
    StartupFailed,
}

internal sealed record HorseIdentityMigrationJournalOperation(
    Guid OperationId,
    string RequestHash,
    HorseIdentityMigrationOperationStatus Status,
    HorseIdentityMigrationJournalReason Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<HorseIdentityMigrationJournalResultItem> Results);

internal sealed record HorseIdentityMigrationJournalResultItem(
    HorseIdentityMigrationResultCode Code,
    MigrationStoreSurface? Surface = null,
    int Count = 0);

internal sealed record HorseIdentityMigrationPageCheckpoint(
    TypedHorseIdentityPageEvidence Evidence,
    string MembershipHash,
    HorseIdentityMigrationPageState State);

internal sealed record HorseIdentityMigrationJournalOutcome(
    HorseIdentityMigrationJournalOperation? Operation,
    HorseIdentityMigrationJournalReason Reason)
{
    internal bool Succeeded => Reason == HorseIdentityMigrationJournalReason.None && Operation is not null;
}

internal sealed record HorseIdentityMigrationRequestKeyRecord(
    Guid RequestKey,
    string ImmutableRequestHash,
    string? PreviewFingerprint,
    HorseIdentityMigrationAdmissionKind RequestKind,
    HorseIdentityMigrationJournalOperation Operation);

internal sealed record HorseIdentityMigrationExecutionIntentOutcome(
    HorseIdentityMigrationExecutionIntent? Intent,
    HorseIdentityMigrationJournalReason Reason)
{
    internal bool Succeeded => Reason == HorseIdentityMigrationJournalReason.None && Intent is not null;
}

internal sealed record HorseIdentityMigrationPreparationCancellationOutcome(
    HorseIdentityMigrationJournalOperation? Operation,
    HorseIdentityMigrationExecutionIntent? Intent,
    HorseIdentityMigrationJournalReason Reason)
{
    internal bool Succeeded => Reason == HorseIdentityMigrationJournalReason.None
        && Operation is not null && Intent is not null;
}

internal sealed record HorseIdentityMigrationSavedRequest(
    HorseIdentityMigrationManifest Manifest,
    string PreviewFingerprint,
    string ImmutableRequestHash);

internal sealed record HorseIdentityMigrationPendingRequest(
    HorseIdentityMigrationManifest Manifest,
    string ImmutableRequestHash);

internal sealed record HorseIdentityMigrationExecutableRequest(
    HorseIdentityMigrationManifest Manifest,
    string ImmutableRequestHash,
    HorseIdentityMigrationAdmissionKind RequestKind,
    string? PreviewFingerprint);

internal sealed record HorseIdentityMigrationPendingSeal(
    Guid OperationId,
    string ImmutableRequestHash,
    string FullFingerprint,
    string SourceSetHash,
    string BackupManifestHash,
    DateTimeOffset SealedAt);

internal sealed record HorseIdentityMigrationPendingSealOutcome(
    HorseIdentityMigrationPendingSeal? Seal,
    HorseIdentityMigrationJournalReason Reason)
{
    internal bool Succeeded => Reason == HorseIdentityMigrationJournalReason.None && Seal is not null;
}

internal sealed record HorseIdentityMigrationRequestIdentity(
    string ImmutableRequestHash,
    HorseIdentityMigrationAdmissionKind RequestKind,
    string? PreviewFingerprint);

internal enum HorseIdentityMigrationStartupState
{
    Ready,
    OwnerActive,
    Interrupted,
    PartialUnverified,
    Corrupt,
}

internal sealed record HorseIdentityMigrationStartupGuard(
    HorseIdentityMigrationStartupState State,
    Guid? OperationId = null);

internal sealed record HorseIdentityMigrationStagedRequest(
    Guid OperationId,
    HorseIdentityMigrationManifest Manifest);

/// <summary>
/// Bounded, separate operational journal for the future offline migration runner.
/// This type has no source-store writer and is not registered in the application host.
/// </summary>
internal sealed class HorseIdentityMigrationJournal
{
    internal const long MaximumJournalBytes = 64L * 1024 * 1024;
    internal const int MaximumRequestBytes = 1024 * 1024;
    internal const int MaximumResultBytes = 1024 * 1024;
    internal static readonly TimeSpan TerminalRetention = TimeSpan.FromDays(30);

    private const long FailureStatusReserveBytes = 136 * 1024;
    private const long RequestKeyCapacityReservationBytes = 512;
    private const long ExecutionIntentCapacityReservationBytes = 512;
    private const long PendingSealCapacityReservationBytes = 768;
    private const int MaximumSqliteSectorSizeBytes = 64 * 1024;
    private const int RollbackJournalRecordOverheadBytes = 8;
    private const int RollbackJournalHeaderBytes = 24;
    private const string JournalFileName = "horse-identity-migration-journal.db";
    private const string LockFileName = "horse-identity-migration-operation.lock";
    internal const string InitializationMarkerFileName = "horse-identity-migration-initialized.marker";
    internal const string InitializationMarkerContent = "horse-identity-migration-journal-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly string _databasePath;
    private readonly string _lockPath;
    private readonly string _initializationMarkerPath;
    private readonly HashSet<string> _protectedPaths;
    private readonly long _maximumBytes;
    private readonly long _failureReserveBytes;

    internal HorseIdentityMigrationJournal(
        string stateDirectory,
        IEnumerable<string> configuredStorePaths,
        long maximumBytes = MaximumJournalBytes,
        long failureReserveBytes = FailureStatusReserveBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentNullException.ThrowIfNull(configuredStorePaths);
        if (maximumBytes <= 0 || failureReserveBytes < 0 || failureReserveBytes >= maximumBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        _directory = Path.GetFullPath(stateDirectory);
        _databasePath = Path.Combine(_directory, JournalFileName);
        _lockPath = Path.Combine(_directory, LockFileName);
        _initializationMarkerPath = Path.Combine(_directory, InitializationMarkerFileName);
        _protectedPaths = configuredStorePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(PathComparer);
        _maximumBytes = maximumBytes;
        _failureReserveBytes = failureReserveBytes;

        if (_protectedPaths.Contains(_databasePath) || _protectedPaths.Contains(_lockPath)
            || _protectedPaths.Contains(_initializationMarkerPath))
            throw new InvalidOperationException("Journal path conflicts with a configured store.");
    }

    internal string DatabasePath => _databasePath;
    internal string LockPath => _lockPath;
    internal string InitializationMarkerPath => _initializationMarkerPath;

    internal async Task<HorseIdentityMigrationStartupGuard> InspectStartupGuardAsync(
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or SqliteException or ArgumentException or InvalidOperationException)
        {
            return new(HorseIdentityMigrationStartupState.Corrupt);
        }

        FileStream lockHandle;
        try
        {
            lockHandle = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.WriteThrough);
        }
        catch (IOException)
        {
            return new(HorseIdentityMigrationStartupState.OwnerActive);
        }

        await using (lockHandle.ConfigureAwait(false))
        {
            try
            {
                await using var connection = await OpenExistingAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = connection.BeginTransaction();
                await EnsureOperationalSchemaUpgradesAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
                await using (var invalid = connection.CreateCommand())
                {
                    invalid.Transaction = transaction;
                    invalid.CommandText = """
                        SELECT 1 FROM migration_operations
                        WHERE status NOT BETWEEN $minStatus AND $maxStatus
                            OR reason NOT BETWEEN $minReason AND $maxReason
                            OR (status IN ($staged, $running, $succeeded) AND reason <> $none)
                            OR (status IN ($interrupted, $partial) AND reason <> $interruptedReason)
                            OR ((status IN ($staged, $running, $interrupted, $partial) AND terminal_utc IS NOT NULL)
                                OR (status IN ($succeeded, $rejected, $failed) AND terminal_utc IS NULL))
                        LIMIT 1;
                        """;
                    invalid.Parameters.AddWithValue("$minStatus", (int)HorseIdentityMigrationOperationStatus.Staged);
                    invalid.Parameters.AddWithValue("$maxStatus", (int)HorseIdentityMigrationOperationStatus.PartialUnverified);
                    invalid.Parameters.AddWithValue("$minReason", (int)HorseIdentityMigrationJournalReason.None);
                    invalid.Parameters.AddWithValue("$maxReason", (int)HorseIdentityMigrationJournalReason.PreparationCancelled);
                    invalid.Parameters.AddWithValue("$succeeded", (int)HorseIdentityMigrationOperationStatus.Succeeded);
                    invalid.Parameters.AddWithValue("$none", (int)HorseIdentityMigrationJournalReason.None);
                    invalid.Parameters.AddWithValue("$staged", (int)HorseIdentityMigrationOperationStatus.Staged);
                    invalid.Parameters.AddWithValue("$running", (int)HorseIdentityMigrationOperationStatus.Running);
                    invalid.Parameters.AddWithValue("$interrupted", (int)HorseIdentityMigrationOperationStatus.Interrupted);
                    invalid.Parameters.AddWithValue("$partial", (int)HorseIdentityMigrationOperationStatus.PartialUnverified);
                    invalid.Parameters.AddWithValue("$interruptedReason", (int)HorseIdentityMigrationJournalReason.OperationInterrupted);
                    invalid.Parameters.AddWithValue("$rejected", (int)HorseIdentityMigrationOperationStatus.Rejected);
                    invalid.Parameters.AddWithValue("$failed", (int)HorseIdentityMigrationOperationStatus.Failed);
                    if (await invalid.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new InvalidDataException("Migration journal contains an invalid operation state.");
                }

                await using (var incompleteSuccess = connection.CreateCommand())
                {
                    incompleteSuccess.Transaction = transaction;
                    incompleteSuccess.CommandText = """
                        SELECT 1 FROM migration_operations o
                        WHERE o.status = $succeeded AND (
                            NOT EXISTS (SELECT 1 FROM migration_required_pages r WHERE r.operation_id = o.operation_id)
                            OR (SELECT COUNT(*) FROM migration_pages p WHERE p.operation_id = o.operation_id)
                                <> (SELECT COUNT(*) FROM migration_required_pages r WHERE r.operation_id = o.operation_id)
                            OR EXISTS (
                                SELECT 1 FROM migration_required_pages r
                                LEFT JOIN migration_pages p ON p.operation_id = r.operation_id AND p.page_identity = r.page_identity
                                WHERE r.operation_id = o.operation_id AND (p.page_identity IS NULL OR p.surface <> r.surface
                                    OR p.table_name <> r.table_name OR p.row_count <> r.row_count
                                    OR p.membership_hash <> r.membership_hash OR p.before_hash <> r.before_hash
                                    OR p.expected_after_hash <> r.expected_after_hash OR p.state <> $verified)
                            )
                        ) LIMIT 1;
                        """;
                    incompleteSuccess.Parameters.AddWithValue("$succeeded", (int)HorseIdentityMigrationOperationStatus.Succeeded);
                    incompleteSuccess.Parameters.AddWithValue("$verified", (int)HorseIdentityMigrationPageState.Verified);
                    if (await incompleteSuccess.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new InvalidDataException("Succeeded operation has incomplete page evidence.");
                }

                await using (var invalidRequestKind = connection.CreateCommand())
                {
                    invalidRequestKind.Transaction = transaction;
                    invalidRequestKind.CommandText = "SELECT 1 FROM migration_request_keys WHERE request_kind NOT IN ('BoundV1', 'PendingV2') LIMIT 1;";
                    if (await invalidRequestKind.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new InvalidDataException("Migration journal contains an unknown request kind.");
                }

                await using (var invalidCancellationBinding = connection.CreateCommand())
                {
                    invalidCancellationBinding.Transaction = transaction;
                    invalidCancellationBinding.CommandText = """
                        SELECT 1 FROM migration_execution_intents i
                        LEFT JOIN migration_operations o ON o.operation_id = i.operation_id
                        WHERE o.operation_id IS NULL
                            OR i.phase NOT BETWEEN $minPhase AND $maxPhase
                            OR (i.phase = $cancelled AND (
                                typeof(i.cancellation_source_set_hash) <> 'text'
                                OR length(i.cancellation_source_set_hash) <> 64
                                OR i.cancellation_source_set_hash GLOB '*[^0-9A-F]*'
                                OR o.status <> $failed
                                OR o.reason <> $cancelledReason
                                OR o.terminal_utc IS NULL))
                            OR (i.phase <> $cancelled AND i.cancellation_source_set_hash IS NOT NULL)
                            OR (i.cancellation_diagnostic_reason IS NOT NULL
                                AND (i.cancellation_diagnostic_reason <> $preparationFailed
                                    OR i.phase <> $cancelRequested))
                            OR (i.phase = $cancelRequested AND (
                                o.status <> $staged OR o.reason <> $none OR o.terminal_utc IS NOT NULL))
                            OR (o.reason = $cancelledReason AND i.phase <> $cancelled)
                        LIMIT 1;
                        """;
                    invalidCancellationBinding.Parameters.AddWithValue("$minPhase",
                        (int)HorseIdentityMigrationExecutionPhase.IntentRecorded);
                    invalidCancellationBinding.Parameters.AddWithValue("$maxPhase",
                        (int)HorseIdentityMigrationExecutionPhase.PreparationCancelled);
                    invalidCancellationBinding.Parameters.AddWithValue("$cancelled",
                        (int)HorseIdentityMigrationExecutionPhase.PreparationCancelled);
                    invalidCancellationBinding.Parameters.AddWithValue("$cancelRequested",
                        (int)HorseIdentityMigrationExecutionPhase.CancellationRequested);
                    invalidCancellationBinding.Parameters.AddWithValue("$failed",
                        (int)HorseIdentityMigrationOperationStatus.Failed);
                    invalidCancellationBinding.Parameters.AddWithValue("$cancelledReason",
                        (int)HorseIdentityMigrationJournalReason.PreparationCancelled);
                    invalidCancellationBinding.Parameters.AddWithValue("$staged",
                        (int)HorseIdentityMigrationOperationStatus.Staged);
                    invalidCancellationBinding.Parameters.AddWithValue("$none",
                        (int)HorseIdentityMigrationJournalReason.None);
                    invalidCancellationBinding.Parameters.AddWithValue("$preparationFailed",
                        (int)HorseIdentityMigrationJournalReason.PreparationFailed);
                    if (await invalidCancellationBinding.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                        is not null)
                        throw new InvalidDataException("Migration journal cancellation state is inconsistent.");
                }

                await using (var unboundCancellationFailure = connection.CreateCommand())
                {
                    unboundCancellationFailure.Transaction = transaction;
                    unboundCancellationFailure.CommandText = """
                        SELECT 1 FROM migration_operations o
                        WHERE o.reason = $cancelledReason AND NOT EXISTS (
                            SELECT 1 FROM migration_execution_intents i
                            WHERE i.operation_id = o.operation_id AND i.phase = $cancelled
                                AND i.cancellation_source_set_hash IS NOT NULL)
                        LIMIT 1;
                        """;
                    unboundCancellationFailure.Parameters.AddWithValue("$cancelledReason",
                        (int)HorseIdentityMigrationJournalReason.PreparationCancelled);
                    unboundCancellationFailure.Parameters.AddWithValue("$cancelled",
                        (int)HorseIdentityMigrationExecutionPhase.PreparationCancelled);
                    if (await unboundCancellationFailure.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                        is not null)
                        throw new InvalidDataException("Migration journal cancellation receipt is unbound.");
                }

                Guid? runningOperation = null;
                await using (var running = connection.CreateCommand())
                {
                    running.Transaction = transaction;
                    running.CommandText = "SELECT operation_id FROM migration_operations WHERE status = $running ORDER BY operation_id LIMIT 2;";
                    running.Parameters.AddWithValue("$running", (int)HorseIdentityMigrationOperationStatus.Running);
                    await using var reader = await running.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (!Guid.TryParseExact(reader.GetString(0), "D", out var parsed) || parsed == Guid.Empty)
                            throw new InvalidDataException("Migration journal contains an invalid operation ID.");
                        runningOperation = parsed;
                        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            throw new InvalidDataException("Migration journal contains multiple running operations.");
                    }
                }

                if (runningOperation is { } stale)
                {
                    await SetStatusAsync(connection, transaction, stale,
                        HorseIdentityMigrationOperationStatus.Interrupted,
                        HorseIdentityMigrationJournalReason.OperationInterrupted, now, cancellationToken)
                        .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new(HorseIdentityMigrationStartupState.Interrupted, stale);
                }

                await using (var blocked = connection.CreateCommand())
                {
                    blocked.Transaction = transaction;
                    blocked.CommandText = "SELECT operation_id, status FROM migration_operations WHERE status IN ($interrupted, $partial) ORDER BY operation_id LIMIT 1;";
                    blocked.Parameters.AddWithValue("$interrupted", (int)HorseIdentityMigrationOperationStatus.Interrupted);
                    blocked.Parameters.AddWithValue("$partial", (int)HorseIdentityMigrationOperationStatus.PartialUnverified);
                    await using var reader = await blocked.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (!Guid.TryParseExact(reader.GetString(0), "D", out var parsed) || parsed == Guid.Empty)
                            throw new InvalidDataException("Migration journal contains an invalid operation ID.");
                        var status = ReadDefinedEnum<HorseIdentityMigrationOperationStatus>(reader.GetInt32(1), "operation status");
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return new(status == HorseIdentityMigrationOperationStatus.Interrupted
                            ? HorseIdentityMigrationStartupState.Interrupted
                            : HorseIdentityMigrationStartupState.PartialUnverified, parsed);
                    }
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(HorseIdentityMigrationStartupState.Ready);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or SqliteException or FormatException or ArgumentException or InvalidOperationException)
            {
                return new(HorseIdentityMigrationStartupState.Corrupt);
            }
        }
    }

    internal async Task<HorseIdentityMigrationStagedRequest?> GetClaimedStagedRequestAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !File.Exists(_databasePath)) return null;
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        HorseIdentityMigrationOperationStatus status;
        var claimed = false;
        var requestIsBoundedText = false;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT status, workflow_claimed,
                    CASE WHEN typeof(request_hash) = 'text' AND length(CAST(request_hash AS BLOB)) = 64
                        AND request_hash NOT GLOB '*[^0-9A-F]'
                        AND typeof(request_json) = 'text'
                        AND length(CAST(request_json AS BLOB)) <= $maximumRequestBytes THEN 1 ELSE 0 END
                FROM migration_operations WHERE operation_id = $id;
                """;
            command.Parameters.AddWithValue("$maximumRequestBytes", MaximumRequestBytes);
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
            status = ReadDefinedEnum<HorseIdentityMigrationOperationStatus>(reader.GetInt32(0), "operation status");
            claimed = reader.GetInt32(1) == 1;
            requestIsBoundedText = reader.GetInt32(2) == 1;
        }

        if (status != HorseIdentityMigrationOperationStatus.Staged || !claimed)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        if (!requestIsBoundedText)
            throw new InvalidDataException("Migration journal staged request exceeds its stored bounds.");

        string requestHash;
        string requestJson;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT request_hash, request_json FROM migration_operations WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("Migration journal staged request disappeared.");
            requestHash = reader.GetString(0);
            requestJson = reader.GetString(1);
        }
        if (!string.Equals(requestHash,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson))), StringComparison.Ordinal))
            throw new InvalidDataException("Migration journal staged request hash is corrupt.");
        var mappings = JsonSerializer.Deserialize<HorseIdentityMigrationMapping[]>(requestJson, JsonOptions)
            ?? throw new InvalidDataException("Migration journal staged request is corrupt.");
        var manifest = new HorseIdentityMigrationManifest(mappings.Select(mapping =>
            (HorseIdentityMigrationManifestEntry?)new(mapping.OldHorseId, mapping.JraIdentity, true)).ToArray());
        var validation = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        if (!validation.IsValid
            || !string.Equals(JsonSerializer.Serialize(validation.Mappings, JsonOptions), requestJson, StringComparison.Ordinal))
            throw new InvalidDataException("Migration journal staged request is not canonical.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(operationId, manifest);
    }

    internal async Task<HorseIdentityMigrationJournalOperation?> GetStatusAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !File.Exists(_databasePath)) return null;
        await PrepareReadOnlyDirectoryAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation?.Status == HorseIdentityMigrationOperationStatus.Succeeded
            && !await HasCompleteVerifiedEvidenceAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false))
            throw new InvalidDataException("Succeeded operation has incomplete page evidence.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return operation;
    }

    internal async Task<HorseIdentityMigrationJournalOperation?> GetMostRecentOperationAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_databasePath)) return null;
        await PrepareReadOnlyDirectoryAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        Guid? operationId = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT operation_id FROM migration_operations ORDER BY updated_utc DESC, operation_id LIMIT 1;";
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is string id && Guid.TryParseExact(id, "D", out var parsed) && parsed != Guid.Empty)
                operationId = parsed;
            else if (value is not null)
                throw new InvalidDataException("Migration journal operation identity is corrupt.");
        }

        if (operationId is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var operation = await ReadOperationAsync(connection, transaction, operationId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (operation?.Status == HorseIdentityMigrationOperationStatus.Succeeded
            && !await HasCompleteVerifiedEvidenceAsync(connection, transaction, operationId.Value, cancellationToken)
                .ConfigureAwait(false))
            throw new InvalidDataException("Succeeded operation has incomplete page evidence.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return operation;
    }

    internal async Task<HorseIdentityMigrationSavedRequest?> GetSavedRequestAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !File.Exists(_databasePath)) return null;
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        string? requestJson = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT request_json FROM migration_operations WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            requestJson = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }

        if (requestJson is null || !await HasRequestKeyTableAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var requestKind = await ReadRequestKindForOperationAsync(connection, transaction, operationId,
            cancellationToken).ConfigureAwait(false);
        if (requestKind != HorseIdentityMigrationAdmissionKind.BoundV1)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        string? immutableRequestHash = null;
        string? fingerprint = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT immutable_request_hash, preview_fingerprint FROM migration_request_keys WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                immutableRequestHash = reader.GetString(0);
                fingerprint = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }
        if (immutableRequestHash is null || fingerprint is null || !IsSha256Hash(fingerprint))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var mappings = JsonSerializer.Deserialize<HorseIdentityMigrationMapping[]>(requestJson, JsonOptions);
        var manifest = mappings is null ? null : new HorseIdentityMigrationManifest(mappings.Select(mapping =>
            (HorseIdentityMigrationManifestEntry?)new(mapping.OldHorseId, mapping.JraIdentity, true)).ToArray());
        if (manifest is null || !HorseIdentityMigrationManifestPolicy.Validate(manifest).IsValid)
            throw new InvalidDataException("Saved migration request is invalid.");
        var expectedHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        if (!string.Equals(immutableRequestHash, expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("Saved migration request binding is inconsistent.");

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(manifest, fingerprint, expectedHash);
    }

    internal async Task<HorseIdentityMigrationPendingRequest?> GetPendingRequestAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !File.Exists(_databasePath)) return null;
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        string? requestJson;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT request_json FROM migration_operations WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            requestJson = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }

        if (requestJson is null || !await HasRequestKeyTableAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        string? immutableHash = null;
        string? fingerprint = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT immutable_request_hash, preview_fingerprint FROM migration_request_keys WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                immutableHash = reader.GetString(0);
                fingerprint = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        var requestKind = await ReadRequestKindForOperationAsync(connection, transaction, operationId,
            cancellationToken).ConfigureAwait(false);
        if (requestKind != HorseIdentityMigrationAdmissionKind.PendingV2 || fingerprint is not null
            || !IsSha256Hash(immutableHash))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var mappings = JsonSerializer.Deserialize<HorseIdentityMigrationMapping[]>(requestJson, JsonOptions);
        var manifest = mappings is null ? null : new HorseIdentityMigrationManifest(mappings.Select(mapping =>
            (HorseIdentityMigrationManifestEntry?)new(mapping.OldHorseId, mapping.JraIdentity, true)).ToArray());
        if (manifest is null || !HorseIdentityMigrationManifestPolicy.Validate(manifest).IsValid
            || !string.Equals(JsonSerializer.Serialize(mappings, JsonOptions), requestJson, StringComparison.Ordinal))
            throw new InvalidDataException("Saved pending migration request is invalid.");
        var expectedHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        if (!string.Equals(immutableHash, expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("Saved pending migration request identity is inconsistent.");

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(manifest, expectedHash);
    }

    internal async Task<HorseIdentityMigrationExecutableRequest?> GetExecutableRequestAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        var saved = await GetSavedRequestAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (saved is not null)
            return new(saved.Manifest, saved.ImmutableRequestHash, HorseIdentityMigrationAdmissionKind.BoundV1,
                saved.PreviewFingerprint);

        var pending = await GetPendingRequestAsync(operationId, cancellationToken).ConfigureAwait(false);
        return pending is null ? null : new(pending.Manifest, pending.ImmutableRequestHash,
            HorseIdentityMigrationAdmissionKind.PendingV2, PreviewFingerprint: null);
    }

    internal async Task<HorseIdentityMigrationPendingSeal?> GetPendingSealAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !File.Exists(_databasePath)) return null;
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var seal = await HasPendingSealTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
            ? await ReadPendingSealAsync(connection, transaction, operationId, cancellationToken).ConfigureAwait(false)
            : null;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return seal;
    }

    internal async Task<HorseIdentityMigrationPendingSealOutcome> SealPendingRequestAsync(
        Guid operationId, string immutableRequestHash, string fullFingerprint, string sourceSetHash,
        string backupManifestHash, DateTimeOffset sealedAt, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !IsSha256Hash(immutableRequestHash) || !IsSha256Hash(fullFingerprint)
            || !IsSha256Hash(sourceSetHash) || !IsSha256Hash(backupManifestHash))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var existing = await ReadPendingSealAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return SealMatches(existing, immutableRequestHash, fullFingerprint, sourceSetHash, backupManifestHash)
                ? new(existing, HorseIdentityMigrationJournalReason.None)
                : new(existing, HorseIdentityMigrationJournalReason.RequestConflict);
        }

        var request = await ReadExecutableRequestIdentityAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var intent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (request is null || request.RequestKind != HorseIdentityMigrationAdmissionKind.PendingV2
            || request.PreviewFingerprint is not null
            || !string.Equals(request.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal)
            || operation?.Status != HorseIdentityMigrationOperationStatus.Staged
            || intent?.Phase != HorseIdentityMigrationExecutionPhase.BackedUp
            || !string.Equals(intent.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal))
            return new(null, HorseIdentityMigrationJournalReason.StaleOperation);

        if (!await HasCapacityAsync(connection, transaction, PendingSealCapacityReservationBytes, cancellationToken)
                .ConfigureAwait(false))
            return new(null, HorseIdentityMigrationJournalReason.CapacityExceeded);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO migration_pending_seals (operation_id, immutable_request_hash, full_fingerprint, source_set_hash, backup_manifest_hash, sealed_utc) VALUES ($id, $request, $fingerprint, $source, $backup, $sealed);";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$request", immutableRequestHash);
            command.Parameters.AddWithValue("$fingerprint", fullFingerprint);
            command.Parameters.AddWithValue("$source", sourceSetHash);
            command.Parameters.AddWithValue("$backup", backupManifestHash);
            command.Parameters.AddWithValue("$sealed", sealedAt.ToUniversalTime().ToString("O"));
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                return new(null, HorseIdentityMigrationJournalReason.OwnershipBusy);
            }
        }

        var seal = await ReadPendingSealAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return seal is null
            ? new(null, HorseIdentityMigrationJournalReason.StaleOperation)
            : new(seal, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationRequestKeyRecord?> GetByRequestKeyAsync(
        Guid requestKey,
        CancellationToken cancellationToken = default)
    {
        if (requestKey == Guid.Empty || !File.Exists(_databasePath)) return null;
        await PrepareReadOnlyDirectoryAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        if (!await HasRequestKeyTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var key = await ReadRequestKeyAsync(connection, transaction, requestKey, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var keyValue = key.Value;
        var operation = await ReadOperationAsync(connection, transaction, keyValue.OperationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null)
            throw new InvalidDataException("Migration journal request key refers to a missing operation.");
        if (operation.Status == HorseIdentityMigrationOperationStatus.Succeeded
            && !await HasCompleteVerifiedEvidenceAsync(connection, transaction, operation.OperationId, cancellationToken)
                .ConfigureAwait(false))
            throw new InvalidDataException("Succeeded operation has incomplete page evidence.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(requestKey, keyValue.ImmutableRequestHash, keyValue.PreviewFingerprint,
            keyValue.RequestKind, operation);
    }

    internal async Task<HorseIdentityMigrationJournalOutcome> StageAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await StageCoreAsync(operationId, manifest, null, null, null,
            HorseIdentityMigrationAdmissionKind.BoundV1, now, cancellationToken).ConfigureAwait(false);

    internal async Task<HorseIdentityMigrationJournalOutcome> StageWithRequestKeyAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        Guid requestKey,
        string immutableRequestHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => await StageWithRequestKeyAsync(operationId, manifest, requestKey, immutableRequestHash, null, now,
            cancellationToken).ConfigureAwait(false);

    internal async Task<HorseIdentityMigrationJournalOutcome> StageWithRequestKeyAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        Guid requestKey,
        string immutableRequestHash,
        string? previewFingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => await StageWithRequestKeyAsync(operationId, manifest, requestKey, immutableRequestHash,
            previewFingerprint, HorseIdentityMigrationAdmissionKind.BoundV1, now, cancellationToken)
            .ConfigureAwait(false);

    internal async Task<HorseIdentityMigrationJournalOutcome> StagePendingWithRequestKeyAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        Guid requestKey,
        string immutableRequestHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await StageWithRequestKeyAsync(operationId, manifest, requestKey, immutableRequestHash, null,
            HorseIdentityMigrationAdmissionKind.PendingV2, now, cancellationToken).ConfigureAwait(false);

    private async Task<HorseIdentityMigrationJournalOutcome> StageWithRequestKeyAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        Guid requestKey,
        string immutableRequestHash,
        string? previewFingerprint,
        HorseIdentityMigrationAdmissionKind requestKind,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (requestKey == Guid.Empty || manifest is null || !IsSha256Hash(immutableRequestHash)
            || !Enum.IsDefined(requestKind)
            || (requestKind == HorseIdentityMigrationAdmissionKind.PendingV2
                && (previewFingerprint is not null
                    || !string.Equals(immutableRequestHash,
                        HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest),
                        StringComparison.Ordinal))))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await StageCoreAsync(operationId, manifest, requestKey, immutableRequestHash, previewFingerprint,
                    requestKind, now, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6 or 19)
            {
                if (attempt >= 12)
                    return new(null, HorseIdentityMigrationJournalReason.OwnershipBusy);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
                when (exception.Message == "Initialized migration journal is missing.")
            {
                if (attempt >= 12)
                    return new(null, HorseIdentityMigrationJournalReason.OwnershipBusy);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    internal async Task<HorseIdentityMigrationJournalOutcome> BindStandaloneRequestAsync(
        Guid operationId, HorseIdentityMigrationManifest? manifest, string previewFingerprint,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || manifest is null || !IsSha256Hash(previewFingerprint))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);
        var validation = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        if (!validation.IsValid) return new(null, HorseIdentityMigrationJournalReason.RequestConflict);
        var immutableRequestHash = HorseIdentityMigrationExecutionPolicy
            .ComputeImmutableRequestHash(manifest, previewFingerprint);
        var requestJson = JsonSerializer.Serialize(validation.Mappings, JsonOptions);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation?.Status != HorseIdentityMigrationOperationStatus.Staged)
            return new(operation, HorseIdentityMigrationJournalReason.StaleOperation);

        if (await ReadRequestKindForOperationAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false) == HorseIdentityMigrationAdmissionKind.PendingV2)
            return new(operation, HorseIdentityMigrationJournalReason.RequestConflict);

        var storedRequestHash = await ReadRequestHashAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (storedRequestHash is null
            || !string.Equals(storedRequestHash,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson))), StringComparison.Ordinal))
            return new(operation, HorseIdentityMigrationJournalReason.RequestConflict);

        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT immutable_request_hash, preview_fingerprint FROM migration_request_keys WHERE operation_id = $id;";
            existing.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var sameBinding = string.Equals(reader.GetString(0), immutableRequestHash, StringComparison.Ordinal)
                    && !reader.IsDBNull(1)
                    && string.Equals(reader.GetString(1), previewFingerprint, StringComparison.Ordinal);
                await reader.DisposeAsync().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return sameBinding
                    ? new(operation, HorseIdentityMigrationJournalReason.None)
                    : new(operation, HorseIdentityMigrationJournalReason.RequestConflict);
            }
        }

        if (!await HasCapacityAsync(connection, transaction, RequestKeyCapacityReservationBytes,
                cancellationToken).ConfigureAwait(false))
            return new(operation, HorseIdentityMigrationJournalReason.CapacityExceeded);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO migration_request_keys (request_key, immutable_request_hash, preview_fingerprint, request_kind, operation_id) VALUES ($key, $hash, $fingerprint, $kind, $id);";
            insert.Parameters.AddWithValue("$key", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$hash", immutableRequestHash);
            insert.Parameters.AddWithValue("$fingerprint", previewFingerprint);
            insert.Parameters.AddWithValue("$kind", HorseIdentityMigrationAdmissionKind.BoundV1.ToString());
            insert.Parameters.AddWithValue("$id", operationId.ToString("D"));
            try
            {
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                return new(operation, HorseIdentityMigrationJournalReason.OwnershipBusy);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(operation, HorseIdentityMigrationJournalReason.None);
    }

    private async Task<HorseIdentityMigrationJournalOutcome> StageCoreAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        Guid? requestKey,
        string? immutableRequestHash,
        string? previewFingerprint,
        HorseIdentityMigrationAdmissionKind requestKind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || manifest is null || !Enum.IsDefined(requestKind))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        var validation = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        if (!validation.IsValid)
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        var requestJson = JsonSerializer.Serialize(validation.Mappings, JsonOptions);
        var requestBytes = Encoding.UTF8.GetByteCount(requestJson);
        if (requestBytes > MaximumRequestBytes)
            return new(null, HorseIdentityMigrationJournalReason.CapacityExceeded);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        await PurgeEligibleTerminalAsync(connection, transaction, now, cancellationToken).ConfigureAwait(false);

        if (requestKey is { } key)
        {
            var existingKey = await ReadRequestKeyAsync(connection, transaction, key, cancellationToken)
                .ConfigureAwait(false);
            if (existingKey is not null)
            {
                var existingKeyValue = existingKey.Value;
                var keyedOperation = await ReadOperationAsync(connection, transaction, existingKeyValue.OperationId, cancellationToken)
                    .ConfigureAwait(false);
                if (keyedOperation is null)
                    throw new InvalidDataException("Migration journal request key refers to a missing operation.");
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return existingKeyValue.RequestKind == requestKind
                    && string.Equals(existingKeyValue.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal)
                    ? new(keyedOperation, HorseIdentityMigrationJournalReason.None)
                    : new(keyedOperation, HorseIdentityMigrationJournalReason.RequestConflict);
            }
        }

        var existing = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (requestKey is not null)
                return new(existing, HorseIdentityMigrationJournalReason.RequestConflict);
            var existingRequest = await ReadRequestHashAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(existingRequest, requestHash, StringComparison.Ordinal)
                ? new(existing, HorseIdentityMigrationJournalReason.None)
                : new(existing, HorseIdentityMigrationJournalReason.RequestConflict);
        }

        var requestKeyBytes = requestKey is null ? 0 : RequestKeyCapacityReservationBytes;
        if (!await HasCapacityAsync(connection, transaction, checked(requestBytes + requestKeyBytes),
                cancellationToken).ConfigureAwait(false))
            return new(null, HorseIdentityMigrationJournalReason.CapacityExceeded);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO migration_operations
                    (operation_id, request_hash, request_json, status, reason, created_utc, updated_utc,
                     terminal_utc, results_json, workflow_claimed)
                VALUES ($id, $hash, $request, $status, $reason, $created, $updated, NULL, '[]', 0);
                """;
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$hash", requestHash);
            command.Parameters.AddWithValue("$request", requestJson);
            command.Parameters.AddWithValue("$status", (int)HorseIdentityMigrationOperationStatus.Staged);
            command.Parameters.AddWithValue("$reason", (int)HorseIdentityMigrationJournalReason.None);
            command.Parameters.AddWithValue("$created", now.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$updated", now.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (requestKey is { } newKey)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO migration_request_keys (request_key, immutable_request_hash, preview_fingerprint, request_kind, operation_id) VALUES ($key, $hash, $fingerprint, $kind, $id);";
            command.Parameters.AddWithValue("$key", newKey.ToString("D"));
            command.Parameters.AddWithValue("$hash", immutableRequestHash!);
            command.Parameters.AddWithValue("$fingerprint", (object?)previewFingerprint ?? DBNull.Value);
            command.Parameters.AddWithValue("$kind", requestKind.ToString());
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var staged = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(staged, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationExecutionIntentOutcome> CreateExecutionIntentAsync(
        Guid operationId,
        string immutableRequestHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !IsSha256Hash(immutableRequestHash))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var existing = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(existing.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal)
                ? new(existing, HorseIdentityMigrationJournalReason.None)
                : new(existing, HorseIdentityMigrationJournalReason.RequestConflict);
        }

        var key = await ReadRequestKeyForOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (!HorseIdentityMigrationExecutionPolicy.CanCreateIntent(operation?.Status, existing is not null)
            || key is null
            || !string.Equals(key, immutableRequestHash, StringComparison.Ordinal))
            return new(null, HorseIdentityMigrationJournalReason.StaleOperation);

        await using (var activeCommand = connection.CreateCommand())
        {
            activeCommand.Transaction = transaction;
            activeCommand.CommandText = "SELECT operation_id FROM migration_execution_intents WHERE phase NOT IN ($verified, $failed, $cancelled) AND operation_id <> $id LIMIT 1;";
            activeCommand.Parameters.AddWithValue("$verified", (int)HorseIdentityMigrationExecutionPhase.Verified);
            activeCommand.Parameters.AddWithValue("$failed", (int)HorseIdentityMigrationExecutionPhase.Failed);
            activeCommand.Parameters.AddWithValue("$cancelled", (int)HorseIdentityMigrationExecutionPhase.PreparationCancelled);
            activeCommand.Parameters.AddWithValue("$id", operationId.ToString("D"));
            if (await activeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                return new(null, HorseIdentityMigrationJournalReason.OwnershipBusy);
        }

        if (!await HasCapacityAsync(connection, transaction, ExecutionIntentCapacityReservationBytes,
                cancellationToken).ConfigureAwait(false))
            return new(null, HorseIdentityMigrationJournalReason.CapacityExceeded);

        var timestamp = now.ToUniversalTime().ToString("O");
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO migration_execution_intents (operation_id, immutable_request_hash, created_utc, updated_utc, phase) VALUES ($id, $hash, $created, $updated, $phase);";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$hash", immutableRequestHash);
            command.Parameters.AddWithValue("$created", timestamp);
            command.Parameters.AddWithValue("$updated", timestamp);
            command.Parameters.AddWithValue("$phase", (int)HorseIdentityMigrationExecutionPhase.IntentRecorded);
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                return new(null, HorseIdentityMigrationJournalReason.OwnershipBusy);
            }
        }

        var intent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(intent, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationExecutionIntent?> GetExecutionIntentAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !File.Exists(_databasePath)) return null;
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        if (!await HasExecutionIntentTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        var intent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return intent;
    }

    internal async Task<HorseIdentityMigrationExecutionIntent?> GetActiveExecutionIntentAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_databasePath)) return null;
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        if (!await HasExecutionIntentTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        Guid? operationId = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT operation_id FROM migration_execution_intents WHERE phase NOT IN ($verified, $failed, $cancelled) LIMIT 2;";
            command.Parameters.AddWithValue("$verified", (int)HorseIdentityMigrationExecutionPhase.Verified);
            command.Parameters.AddWithValue("$failed", (int)HorseIdentityMigrationExecutionPhase.Failed);
            command.Parameters.AddWithValue("$cancelled", (int)HorseIdentityMigrationExecutionPhase.PreparationCancelled);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!Guid.TryParseExact(reader.GetString(0), "D", out var parsed) || parsed == Guid.Empty)
                    throw new InvalidDataException("Migration journal contains an invalid active operation ID.");
                operationId = parsed;
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("Migration journal contains multiple active execution intents.");
            }
        }

        var intent = operationId is { } id
            ? await ReadExecutionIntentAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false)
            : null;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return intent;
    }

    internal async Task<HorseIdentityMigrationPreparationCancellationOutcome> RequestPreparationCancellationAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            return new(null, null, HorseIdentityMigrationJournalReason.StaleOperation);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var intent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var requestHash = await ReadRequestKeyForOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var requestKind = await ReadRequestKindForOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null || intent is null || requestHash is null
            || !HorseIdentityMigrationExecutionPolicy.IsDefinedAdmissionKind(requestKind)
            || !string.Equals(requestHash, intent.ImmutableRequestHash, StringComparison.Ordinal))
            return new(operation, intent, HorseIdentityMigrationJournalReason.StaleOperation);

        if (intent.Phase is HorseIdentityMigrationExecutionPhase.CancellationRequested
            or HorseIdentityMigrationExecutionPhase.PreparationCancelled)
        {
            var duplicateIsConsistent = intent.Phase == HorseIdentityMigrationExecutionPhase.CancellationRequested
                ? operation.Status == HorseIdentityMigrationOperationStatus.Staged
                    && operation.Reason == HorseIdentityMigrationJournalReason.None
                : operation.Status == HorseIdentityMigrationOperationStatus.Failed
                    && operation.Reason == HorseIdentityMigrationJournalReason.PreparationCancelled
                    && intent.CancellationSourceSetHash is not null;
            if (!duplicateIsConsistent)
                return new(operation, intent, HorseIdentityMigrationJournalReason.RequestConflict);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(operation, intent, HorseIdentityMigrationJournalReason.None);
        }

        if (!HorseIdentityMigrationExecutionPolicy.CanRequestPreparationCancellation(operation.Status, intent.Phase)
            || intent.CancellationSourceSetHash is not null)
            return new(operation, intent, HorseIdentityMigrationJournalReason.StaleOperation);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE migration_execution_intents SET phase = $next, updated_utc = $updated WHERE operation_id = $id AND phase = $current AND cancellation_source_set_hash IS NULL;";
            command.Parameters.AddWithValue("$next", (int)HorseIdentityMigrationExecutionPhase.CancellationRequested);
            command.Parameters.AddWithValue("$updated", now.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$current", (int)intent.Phase);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return new(operation, intent, HorseIdentityMigrationJournalReason.OwnershipBusy);
        }

        var updatedIntent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var updatedOperation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(updatedOperation, updatedIntent, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationPreparationCancellationOutcome> RecordPreparationCancellationDiagnosticAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            return new(null, null, HorseIdentityMigrationJournalReason.StaleOperation);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var intent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var requestHash = await ReadRequestKeyForOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var requestKind = await ReadRequestKindForOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null || intent is null || requestHash is null
            || !HorseIdentityMigrationExecutionPolicy.IsDefinedAdmissionKind(requestKind)
            || !string.Equals(requestHash, intent.ImmutableRequestHash, StringComparison.Ordinal))
            return new(operation, intent, HorseIdentityMigrationJournalReason.StaleOperation);
        if (intent.Phase != HorseIdentityMigrationExecutionPhase.CancellationRequested
            || operation.Status != HorseIdentityMigrationOperationStatus.Staged
            || operation.Reason != HorseIdentityMigrationJournalReason.None
            || intent.CancellationSourceSetHash is not null)
            return new(operation, intent, HorseIdentityMigrationJournalReason.StaleOperation);

        if (intent.CancellationDiagnosticReason == HorseIdentityMigrationJournalReason.PreparationFailed)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(operation, intent, HorseIdentityMigrationJournalReason.None);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE migration_execution_intents SET cancellation_diagnostic_reason = $reason, updated_utc = $updated WHERE operation_id = $id AND phase = $phase AND cancellation_source_set_hash IS NULL AND cancellation_diagnostic_reason IS NULL;";
            command.Parameters.AddWithValue("$reason", (int)HorseIdentityMigrationJournalReason.PreparationFailed);
            command.Parameters.AddWithValue("$updated", now.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$phase", (int)HorseIdentityMigrationExecutionPhase.CancellationRequested);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return new(operation, intent, HorseIdentityMigrationJournalReason.OwnershipBusy);
        }

        var updatedIntent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(operation, updatedIntent, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationPreparationCancellationOutcome> FinalizePreparationCancellationAsync(
        Guid operationId,
        HorseIdentityMigrationMaintenanceCapability.PreparationCancellationAuthorization authorization,
        IEnumerable<string> configuredSourcePaths,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(configuredSourcePaths);
        if (operationId == Guid.Empty)
            return new(null, null, HorseIdentityMigrationJournalReason.StaleOperation);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var intent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var requestHash = await ReadRequestKeyForOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null || intent is null || requestHash is null
            || !string.Equals(requestHash, intent.ImmutableRequestHash, StringComparison.Ordinal)
            || authorization.OperationId != operationId
            || !string.Equals(authorization.RequestHash, intent.ImmutableRequestHash, StringComparison.Ordinal))
            return new(operation, intent, HorseIdentityMigrationJournalReason.StaleOperation);

        if (intent.Phase == HorseIdentityMigrationExecutionPhase.PreparationCancelled)
        {
            if (operation.Status != HorseIdentityMigrationOperationStatus.Failed
                || operation.Reason != HorseIdentityMigrationJournalReason.PreparationCancelled
                || !HorseIdentityMigrationMaintenanceCapability.IsHash(intent.CancellationSourceSetHash ?? string.Empty)
                || !string.Equals(intent.CancellationSourceSetHash, authorization.SourceSetHash, StringComparison.Ordinal)
                || !string.Equals(intent.CancellationSourceSetHash,
                    HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(configuredSourcePaths),
                    StringComparison.Ordinal))
                return new(operation, intent, HorseIdentityMigrationJournalReason.RequestConflict);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(operation, intent, HorseIdentityMigrationJournalReason.None);
        }

        if (!HorseIdentityMigrationExecutionPolicy.CanFinalizePreparationCancellation(operation.Status, intent.Phase)
            || intent.CancellationSourceSetHash is not null
            || !HorseIdentityMigrationMaintenanceCapability.IsHash(authorization.SourceSetHash)
            || !authorization.ValidateForSourcePaths(configuredSourcePaths))
            return new(operation, intent, HorseIdentityMigrationJournalReason.StaleOperation);

        var timestamp = now.ToUniversalTime().ToString("O");
        await using (var advanceIntent = connection.CreateCommand())
        {
            advanceIntent.Transaction = transaction;
            advanceIntent.CommandText = "UPDATE migration_execution_intents SET phase = $next, updated_utc = $updated, cancellation_source_set_hash = $sourceHash, cancellation_diagnostic_reason = NULL WHERE operation_id = $id AND phase = $current AND cancellation_source_set_hash IS NULL;";
            advanceIntent.Parameters.AddWithValue("$next", (int)HorseIdentityMigrationExecutionPhase.PreparationCancelled);
            advanceIntent.Parameters.AddWithValue("$updated", timestamp);
            advanceIntent.Parameters.AddWithValue("$sourceHash", authorization.SourceSetHash);
            advanceIntent.Parameters.AddWithValue("$id", operationId.ToString("D"));
            advanceIntent.Parameters.AddWithValue("$current", (int)HorseIdentityMigrationExecutionPhase.CancellationRequested);
            if (await advanceIntent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return new(operation, intent, HorseIdentityMigrationJournalReason.OwnershipBusy);
        }

        await using (var failOperation = connection.CreateCommand())
        {
            failOperation.Transaction = transaction;
            failOperation.CommandText = "UPDATE migration_operations SET status = $failed, reason = $reason, updated_utc = $updated, terminal_utc = $terminal WHERE operation_id = $id AND status = $staged AND reason = $none AND terminal_utc IS NULL;";
            failOperation.Parameters.AddWithValue("$failed", (int)HorseIdentityMigrationOperationStatus.Failed);
            failOperation.Parameters.AddWithValue("$reason", (int)HorseIdentityMigrationJournalReason.PreparationCancelled);
            failOperation.Parameters.AddWithValue("$updated", timestamp);
            failOperation.Parameters.AddWithValue("$terminal", timestamp);
            failOperation.Parameters.AddWithValue("$id", operationId.ToString("D"));
            failOperation.Parameters.AddWithValue("$staged", (int)HorseIdentityMigrationOperationStatus.Staged);
            failOperation.Parameters.AddWithValue("$none", (int)HorseIdentityMigrationJournalReason.None);
            if (await failOperation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return new(operation, intent, HorseIdentityMigrationJournalReason.OwnershipBusy);
        }

        var finalizedIntent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var finalizedOperation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(finalizedOperation, finalizedIntent, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationExecutionIntentOutcome> AdvanceExecutionIntentAsync(
        Guid operationId,
        HorseIdentityMigrationExecutionPhase nextPhase,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            return new(null, HorseIdentityMigrationJournalReason.StaleOperation);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var current = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var requestHash = await ReadRequestKeyForOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var requestKind = await ReadRequestKindForOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null || operation is null
            || requestHash is null
            || !string.Equals(requestHash, current.ImmutableRequestHash, StringComparison.Ordinal)
            || nextPhase is HorseIdentityMigrationExecutionPhase.CancellationRequested
                or HorseIdentityMigrationExecutionPhase.PreparationCancelled
            || !HorseIdentityMigrationExecutionPolicy.CanTransition(current.Phase, nextPhase)
            || ((nextPhase is HorseIdentityMigrationExecutionPhase.Applying
                or HorseIdentityMigrationExecutionPhase.DataSucceeded
                or HorseIdentityMigrationExecutionPhase.Verifying
                or HorseIdentityMigrationExecutionPhase.Verified
                ) && ((nextPhase == HorseIdentityMigrationExecutionPhase.Applying
                    && operation.Status != HorseIdentityMigrationOperationStatus.Staged)
                    || (nextPhase != HorseIdentityMigrationExecutionPhase.Applying
                        && operation.Status != HorseIdentityMigrationOperationStatus.Succeeded))))
            return new(current, HorseIdentityMigrationJournalReason.StaleOperation);

        if (nextPhase == HorseIdentityMigrationExecutionPhase.Applying
            && requestKind == HorseIdentityMigrationAdmissionKind.PendingV2)
        {
            var seal = await ReadPendingSealAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false);
            if (seal is null || !string.Equals(seal.ImmutableRequestHash, current.ImmutableRequestHash,
                    StringComparison.Ordinal))
                return new(current, HorseIdentityMigrationJournalReason.StaleOperation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE migration_execution_intents SET phase = $phase, updated_utc = $updated WHERE operation_id = $id AND phase = $current;";
            command.Parameters.AddWithValue("$phase", (int)nextPhase);
            command.Parameters.AddWithValue("$updated", now.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$current", (int)current.Phase);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return new(current, HorseIdentityMigrationJournalReason.OwnershipBusy);
        }

        var advanced = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(advanced, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<bool> CanBeginReadonlyVerificationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !File.Exists(_databasePath)) return false;
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        var intent = await HasExecutionIntentTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
            ? await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken).ConfigureAwait(false)
            : null;
        var canVerify = operation is not null && intent is not null
            && HorseIdentityMigrationExecutionPolicy.CanBeginReadonlyVerification(operation.Status, intent.Phase);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return canVerify;
    }

    internal async Task<HorseIdentityMigrationJournalOutcome> ClaimWorkflowAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) return new(null, HorseIdentityMigrationJournalReason.StaleOperation);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var current = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
            return new(null, HorseIdentityMigrationJournalReason.StaleOperation);
        if (current.Status != HorseIdentityMigrationOperationStatus.Staged)
            return new(current, current.Status == HorseIdentityMigrationOperationStatus.Running
                ? HorseIdentityMigrationJournalReason.OwnershipBusy
                : HorseIdentityMigrationJournalReason.StaleOperation);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE migration_operations SET workflow_claimed = 1, updated_utc = $now WHERE operation_id = $id AND status = $staged;";
            command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$staged", (int)HorseIdentityMigrationOperationStatus.Staged);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return new(current, HorseIdentityMigrationJournalReason.OwnershipBusy);
        }

        var claimed = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(claimed, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationExecutionLease?> TryAcquireExecutionAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) return null;
        await PrepareDirectoryAsync(cancellationToken).ConfigureAwait(false);

        FileStream lockHandle;
        try
        {
            lockHandle = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.WriteThrough);
        }
        catch (IOException)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();
            await MarkAbandonedRunningAsync(connection, transaction, operationId, now, cancellationToken)
                .ConfigureAwait(false);

            var current = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false);
            if (current is null || current.Status != HorseIdentityMigrationOperationStatus.Staged
                || !await IsWorkflowClaimedAsync(connection, transaction, operationId, cancellationToken)
                    .ConfigureAwait(false))
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                lockHandle.Dispose();
                return null;
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE migration_operations SET status = $running, reason = $none, updated_utc = $now WHERE operation_id = $id AND status = $staged;";
                command.Parameters.AddWithValue("$running", (int)HorseIdentityMigrationOperationStatus.Running);
                command.Parameters.AddWithValue("$none", (int)HorseIdentityMigrationJournalReason.None);
                command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
                command.Parameters.AddWithValue("$id", operationId.ToString("D"));
                command.Parameters.AddWithValue("$staged", (int)HorseIdentityMigrationOperationStatus.Staged);
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    lockHandle.Dispose();
                    return null;
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new HorseIdentityMigrationExecutionLease(this, operationId, lockHandle);
        }
        catch
        {
            lockHandle.Dispose();
            throw;
        }
    }

    internal async Task<HorseIdentityMigrationJournalOutcome> RecordPageCheckpointAsync(
        Guid operationId,
        HorseIdentityMigrationPageCheckpoint checkpoint,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateCheckpoint(checkpoint))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        var pageIdentity = BuildPageIdentity(checkpoint.Evidence);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation?.Status != HorseIdentityMigrationOperationStatus.Running)
            return new(operation, HorseIdentityMigrationJournalReason.StaleOperation);

        var required = await ReadRequiredPageAsync(connection, transaction, operationId, pageIdentity, cancellationToken)
            .ConfigureAwait(false);
        if (required is null || !SameCheckpoint(required, checkpoint))
            return new(operation, HorseIdentityMigrationJournalReason.RequestConflict);

        var prior = await ReadCheckpointAsync(connection, transaction, operationId, pageIdentity, cancellationToken)
            .ConfigureAwait(false);
        if (prior is not null)
        {
            if (!SameCheckpoint(prior, checkpoint))
                return new(operation, HorseIdentityMigrationJournalReason.RequestConflict);
            if ((int)checkpoint.State < (int)prior.State)
                return new(operation, HorseIdentityMigrationJournalReason.StaleOperation);
        }
        else if (checkpoint.State != HorseIdentityMigrationPageState.Prepared)
        {
            return new(operation, HorseIdentityMigrationJournalReason.StaleOperation);
        }

        if (prior is not null && (int)checkpoint.State > (int)prior.State + 1)
            return new(operation, HorseIdentityMigrationJournalReason.StaleOperation);

        var checkpointJson = JsonSerializer.Serialize(checkpoint, JsonOptions);
        var bytes = Encoding.UTF8.GetByteCount(checkpointJson);
        if (bytes > MaximumResultBytes || !await HasCapacityAsync(connection, transaction,
                bytes, cancellationToken).ConfigureAwait(false))
            return new(operation, HorseIdentityMigrationJournalReason.CapacityExceeded);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO migration_pages (operation_id, page_identity, surface, table_name, row_count,
                    membership_hash, before_hash, expected_after_hash, state, updated_utc)
                VALUES ($id, $page, $surface, $table, $rows, $membership, $before, $after, $state, $now)
                ON CONFLICT(operation_id, page_identity) DO UPDATE SET state = excluded.state,
                    updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$page", pageIdentity);
            command.Parameters.AddWithValue("$surface", (int)checkpoint.Evidence.Surface);
            command.Parameters.AddWithValue("$table", checkpoint.Evidence.Table);
            command.Parameters.AddWithValue("$rows", checkpoint.Evidence.RowCount);
            command.Parameters.AddWithValue("$membership", checkpoint.MembershipHash.ToUpperInvariant());
            command.Parameters.AddWithValue("$before", checkpoint.Evidence.BeforeHash.ToUpperInvariant());
            command.Parameters.AddWithValue("$after", checkpoint.Evidence.ExpectedAfterHash.ToUpperInvariant());
            command.Parameters.AddWithValue("$state", (int)checkpoint.State);
            command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(await GetStatusAsync(operationId, cancellationToken).ConfigureAwait(false),
            HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationJournalOutcome> RegisterRequiredPagesAsync(
        Guid operationId,
        IReadOnlyList<HorseIdentityMigrationPageCheckpoint> pages,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0 || pages.Any(page => page.State != HorseIdentityMigrationPageState.Prepared
                || !ValidateCheckpoint(page)))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        var pageDefinitions = pages.Select(page => (Page: page, Identity: BuildPageIdentity(page.Evidence)))
            .OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        if (pageDefinitions.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() != pages.Count)
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);
        var serializedBytes = pageDefinitions.Sum(item => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(item.Page, JsonOptions)));
        if (serializedBytes > MaximumResultBytes)
            return new(null, HorseIdentityMigrationJournalReason.CapacityExceeded);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation?.Status != HorseIdentityMigrationOperationStatus.Running)
            return new(operation, HorseIdentityMigrationJournalReason.StaleOperation);

        var existingCount = await CountRequiredPagesAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (existingCount != 0)
        {
            var existingPages = await ReadRequiredPagesAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existingPages.SequenceEqual(pageDefinitions.Select(item => item.Page), PageCheckpointComparer.Instance)
                ? new(operation, HorseIdentityMigrationJournalReason.None)
                : new(operation, HorseIdentityMigrationJournalReason.RequestConflict);
        }

        var existingResultBytes = await ResultJsonBytesAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (serializedBytes + existingResultBytes > MaximumResultBytes)
            return new(operation, HorseIdentityMigrationJournalReason.CapacityExceeded);

        if (!await HasCapacityAsync(connection, transaction, serializedBytes,
                cancellationToken).ConfigureAwait(false))
            return new(operation, HorseIdentityMigrationJournalReason.CapacityExceeded);

        foreach (var item in pageDefinitions)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO migration_required_pages (operation_id, page_identity, surface, table_name, row_count,
                    membership_hash, before_hash, expected_after_hash, evidence_bytes)
                VALUES ($id, $page, $surface, $table, $rows, $membership, $before, $after, $bytes);
                """;
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            command.Parameters.AddWithValue("$page", item.Identity);
            command.Parameters.AddWithValue("$surface", (int)item.Page.Evidence.Surface);
            command.Parameters.AddWithValue("$table", item.Page.Evidence.Table);
            command.Parameters.AddWithValue("$rows", item.Page.Evidence.RowCount);
            command.Parameters.AddWithValue("$membership", item.Page.MembershipHash.ToUpperInvariant());
            command.Parameters.AddWithValue("$before", item.Page.Evidence.BeforeHash.ToUpperInvariant());
            command.Parameters.AddWithValue("$after", item.Page.Evidence.ExpectedAfterHash.ToUpperInvariant());
            command.Parameters.AddWithValue("$bytes", Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(item.Page, JsonOptions)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(await GetStatusAsync(operationId, cancellationToken).ConfigureAwait(false),
            HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationJournalOutcome> SetResultsAsync(
        Guid operationId,
        IReadOnlyList<HorseIdentityMigrationJournalResultItem> results,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Any(item => !Enum.IsDefined(item.Code) || item.Count < 0
                || (item.Surface.HasValue && !Enum.IsDefined(item.Surface.Value))))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        var resultJson = JsonSerializer.Serialize(results, JsonOptions);
        var bytes = Encoding.UTF8.GetByteCount(resultJson);
        if (bytes > MaximumResultBytes)
            return new(null, HorseIdentityMigrationJournalReason.CapacityExceeded);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null || !IsMutableStatus(operation.Status))
            return new(operation, HorseIdentityMigrationJournalReason.StaleOperation);
        var pageEvidenceBytes = await RequiredEvidenceBytesAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (bytes + pageEvidenceBytes > MaximumResultBytes)
            return new(operation, HorseIdentityMigrationJournalReason.CapacityExceeded);
        if (!await HasCapacityAsync(connection, transaction, bytes,
                cancellationToken).ConfigureAwait(false))
            return new(operation, HorseIdentityMigrationJournalReason.CapacityExceeded);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE migration_operations SET results_json = $results, updated_utc = $now WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$results", resultJson);
            command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var updated = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(updated, HorseIdentityMigrationJournalReason.None);
    }

    internal async Task<HorseIdentityMigrationJournalOutcome> RecordFailureAsync(
        Guid operationId, HorseIdentityMigrationResultCode code, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!IsFailureCode(code))
            return new(null, HorseIdentityMigrationJournalReason.RequestConflict);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var operation = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null)
            return new(null, HorseIdentityMigrationJournalReason.StaleOperation);

        var results = operation.Results.ToList();
        if (!results.Any(result => result.Code == code))
            results.Add(new(code));
        var resultJson = JsonSerializer.Serialize(results, JsonOptions);
        if (Encoding.UTF8.GetByteCount(resultJson) > MaximumResultBytes)
            return new(operation, HorseIdentityMigrationJournalReason.CapacityExceeded);

        var status = operation.Status;
        var reason = operation.Reason;
        if (status == HorseIdentityMigrationOperationStatus.Staged)
        {
            status = HorseIdentityMigrationOperationStatus.Failed;
            reason = ToFailureReason(code);
        }
        else if (status == HorseIdentityMigrationOperationStatus.Running)
        {
            // Once the writer has claimed the operation, its data outcome is uncertain until
            // independent verification says otherwise. Keep the existing interrupted/partial rule.
            status = HorseIdentityMigrationOperationStatus.PartialUnverified;
            reason = HorseIdentityMigrationJournalReason.OperationInterrupted;
        }

        string? priorTerminalAt;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT terminal_utc FROM migration_operations WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            priorTerminalAt = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }
        var terminalAt = IsTerminal(status)
            ? operation.Status == status && priorTerminalAt is not null
                ? priorTerminalAt
                : now.ToUniversalTime().ToString("O")
            : null;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE migration_operations SET status = $status, reason = $reason, results_json = $results, updated_utc = $now, terminal_utc = $terminal WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$status", (int)status);
            command.Parameters.AddWithValue("$reason", (int)reason);
            command.Parameters.AddWithValue("$results", resultJson);
            command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$terminal", (object?)terminalAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var updated = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(updated, HorseIdentityMigrationJournalReason.None);
    }

    internal static HorseIdentityMigrationJournalReason ToFailureReason(HorseIdentityMigrationResultCode code) => code switch
    {
        HorseIdentityMigrationResultCode.PreparationFailed => HorseIdentityMigrationJournalReason.PreparationFailed,
        HorseIdentityMigrationResultCode.BackupFailed => HorseIdentityMigrationJournalReason.BackupFailed,
        HorseIdentityMigrationResultCode.InspectionFailed => HorseIdentityMigrationJournalReason.InspectionFailed,
        HorseIdentityMigrationResultCode.ApplyFailed => HorseIdentityMigrationJournalReason.ApplyFailed,
        HorseIdentityMigrationResultCode.VerificationFailed => HorseIdentityMigrationJournalReason.VerificationFailed,
        HorseIdentityMigrationResultCode.ChildFailed => HorseIdentityMigrationJournalReason.ChildFailed,
        HorseIdentityMigrationResultCode.StartupFailed => HorseIdentityMigrationJournalReason.StartupFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    private static bool IsFailureCode(HorseIdentityMigrationResultCode code) => code is
        HorseIdentityMigrationResultCode.PreparationFailed or HorseIdentityMigrationResultCode.BackupFailed
        or HorseIdentityMigrationResultCode.InspectionFailed or HorseIdentityMigrationResultCode.ApplyFailed
        or HorseIdentityMigrationResultCode.VerificationFailed or HorseIdentityMigrationResultCode.ChildFailed
        or HorseIdentityMigrationResultCode.StartupFailed;

    private async Task CompleteAsync(Guid operationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        if (!await HasCompleteVerifiedEvidenceAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false))
            throw new InvalidOperationException("All typed migration pages must be independently verified before success.");

        // A supervised operation becomes eligible for readonly verification in the same
        // transaction that records its successful data result.  Keeping these writes
        // together prevents a process exit between Succeeded and DataSucceeded from
        // leaving a completed migration that cannot be safely reverified.
        if (await HasExecutionIntentTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            var intent = await ReadExecutionIntentAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false);
            if (intent is not null)
            {
                var requestHash = await ReadRequestKeyForOperationAsync(connection, transaction, operationId,
                    cancellationToken).ConfigureAwait(false);
                if (intent.Phase != HorseIdentityMigrationExecutionPhase.Applying
                    || requestHash is null
                    || !string.Equals(requestHash, intent.ImmutableRequestHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("A supervised operation must complete from its bound Applying intent.");

                await using var advanceIntent = connection.CreateCommand();
                advanceIntent.Transaction = transaction;
                advanceIntent.CommandText = "UPDATE migration_execution_intents SET phase = $phase, updated_utc = $updated WHERE operation_id = $id AND phase = $current;";
                advanceIntent.Parameters.AddWithValue("$phase", (int)HorseIdentityMigrationExecutionPhase.DataSucceeded);
                advanceIntent.Parameters.AddWithValue("$updated", now.ToUniversalTime().ToString("O"));
                advanceIntent.Parameters.AddWithValue("$id", operationId.ToString("D"));
                advanceIntent.Parameters.AddWithValue("$current", (int)HorseIdentityMigrationExecutionPhase.Applying);
                if (await advanceIntent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The supervised execution intent changed before completion.");
            }
        }

        await SetStatusAsync(connection, transaction, operationId, HorseIdentityMigrationOperationStatus.Succeeded,
            HorseIdentityMigrationJournalReason.None, now, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasCompleteVerifiedEvidenceAsync(SqliteConnection connection,
        SqliteTransaction transaction, Guid operationId, CancellationToken cancellationToken)
    {
        var requiredCount = await CountRequiredPagesAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (requiredCount == 0
            || await CountVerifiedPagesAsync(connection, transaction, operationId, cancellationToken).ConfigureAwait(false)
                != requiredCount
            || await CountPagesAsync(connection, transaction, operationId, cancellationToken).ConfigureAwait(false)
                != requiredCount)
            return false;

        await using (var mismatch = connection.CreateCommand())
        {
            mismatch.Transaction = transaction;
            mismatch.CommandText = """
                SELECT COUNT(*) FROM migration_required_pages r
                LEFT JOIN migration_pages p ON p.operation_id = r.operation_id AND p.page_identity = r.page_identity
                WHERE r.operation_id = $id AND (p.page_identity IS NULL OR p.surface <> r.surface
                    OR p.table_name <> r.table_name OR p.row_count <> r.row_count
                    OR p.membership_hash <> r.membership_hash OR p.before_hash <> r.before_hash
                    OR p.expected_after_hash <> r.expected_after_hash OR p.state <> $verified);
                """;
            mismatch.Parameters.AddWithValue("$id", operationId.ToString("D"));
            mismatch.Parameters.AddWithValue("$verified", (int)HorseIdentityMigrationPageState.Verified);
            if (Convert.ToInt32(await mismatch.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
                return false;
        }

        await using var extra = connection.CreateCommand();
        extra.Transaction = transaction;
        extra.CommandText = """
            SELECT COUNT(*) FROM migration_pages p
            LEFT JOIN migration_required_pages r ON r.operation_id = p.operation_id AND r.page_identity = p.page_identity
            WHERE p.operation_id = $id AND r.page_identity IS NULL;
            """;
        extra.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return Convert.ToInt32(await extra.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0;
    }

    private async Task MarkPartialAsync(Guid operationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        await SetStatusAsync(connection, transaction, operationId,
            HorseIdentityMigrationOperationStatus.PartialUnverified,
            HorseIdentityMigrationJournalReason.OperationInterrupted, now, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkAbandonedRunningAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid currentOperationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE migration_operations
            SET status = $interrupted, reason = $reason, updated_utc = $now, terminal_utc = NULL
            WHERE status = $running AND operation_id <> $current;
            """;
        command.Parameters.AddWithValue("$interrupted", (int)HorseIdentityMigrationOperationStatus.Interrupted);
        command.Parameters.AddWithValue("$reason", (int)HorseIdentityMigrationJournalReason.OperationInterrupted);
        command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$running", (int)HorseIdentityMigrationOperationStatus.Running);
        command.Parameters.AddWithValue("$current", currentOperationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var current = connection.CreateCommand();
        current.Transaction = transaction;
        current.CommandText = "SELECT status FROM migration_operations WHERE operation_id = $current;";
        current.Parameters.AddWithValue("$current", currentOperationId.ToString("D"));
        var value = await current.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not null && Convert.ToInt32(value) == (int)HorseIdentityMigrationOperationStatus.Running)
            await SetStatusAsync(connection, transaction, currentOperationId,
                HorseIdentityMigrationOperationStatus.Interrupted,
                HorseIdentityMigrationJournalReason.OperationInterrupted, now, cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task SetStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        HorseIdentityMigrationOperationStatus status,
        HorseIdentityMigrationJournalReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE migration_operations SET status = $status, reason = $reason, updated_utc = $now,
                terminal_utc = $terminal WHERE operation_id = $id AND status = $running;
            """;
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$reason", (int)reason);
        command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$terminal", IsTerminal(status) ? now.ToUniversalTime().ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$running", (int)HorseIdentityMigrationOperationStatus.Running);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsWorkflowClaimedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT workflow_claimed FROM migration_operations WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return (long?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private async Task<string?> ReadRequestHashAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT request_hash FROM migration_operations WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSha256Hash(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static async Task<bool> HasRequestKeyTableAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'migration_request_keys';";
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<bool> HasRequestKeyPreviewFingerprintColumnAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM pragma_table_info('migration_request_keys') WHERE name = 'preview_fingerprint' LIMIT 1;";
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<bool> HasRequestKeyRequestKindColumnAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM pragma_table_info('migration_request_keys') WHERE name = 'request_kind' LIMIT 1;";
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<bool> HasExecutionIntentTableAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'migration_execution_intents';";
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<bool> HasPendingSealTableAsync(
        SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'migration_pending_seals';";
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private async Task<(string ImmutableRequestHash, string? PreviewFingerprint,
        HorseIdentityMigrationAdmissionKind RequestKind, Guid OperationId)?> ReadRequestKeyAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid requestKey,
        CancellationToken cancellationToken)
    {
        if (!await HasRequestKeyTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return null;
        var hasPreviewFingerprint = await HasRequestKeyPreviewFingerprintColumnAsync(connection, transaction,
            cancellationToken).ConfigureAwait(false);
        var hasRequestKind = await HasRequestKeyRequestKindColumnAsync(connection, transaction,
            cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT immutable_request_hash, "
            + (hasPreviewFingerprint ? "preview_fingerprint" : "NULL") + ", "
            + (hasRequestKind ? "request_kind" : "'BoundV1'")
            + ", operation_id FROM migration_request_keys WHERE request_key = $key;";
        command.Parameters.AddWithValue("$key", requestKey.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var hash = reader.GetString(0);
        var fingerprint = !reader.IsDBNull(1) ? reader.GetString(1) : null;
        if (!Enum.TryParse<HorseIdentityMigrationAdmissionKind>(reader.GetString(2), ignoreCase: false,
                out var requestKind) || !Enum.IsDefined(requestKind))
            throw new InvalidDataException("Migration journal request kind is corrupt.");
        const int operationIndex = 3;
        var operationText = reader.GetString(operationIndex);
        if (!IsSha256Hash(hash)
            || (fingerprint is not null && (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)))
            || !Guid.TryParseExact(operationText, "D", out var operationId)
            || operationId == Guid.Empty)
            throw new InvalidDataException("Migration journal request key is corrupt.");
        return (hash, fingerprint, requestKind, operationId);
    }

    private async Task<HorseIdentityMigrationAdmissionKind?> ReadRequestKindForOperationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (!await HasRequestKeyTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return null;
        var hasRequestKind = await HasRequestKeyRequestKindColumnAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = hasRequestKind
            ? "SELECT request_kind FROM migration_request_keys WHERE operation_id = $id;"
            : "SELECT 'BoundV1' FROM migration_request_keys WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        var stored = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (stored is null) return null;
        if (!Enum.TryParse<HorseIdentityMigrationAdmissionKind>(stored, ignoreCase: false, out var kind)
            || !Enum.IsDefined(kind))
            throw new InvalidDataException("Migration journal request kind is corrupt.");
        return kind;
    }

    private async Task<string?> ReadRequestKeyForOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (!await HasRequestKeyTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT immutable_request_hash FROM migration_request_keys WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HorseIdentityMigrationRequestIdentity?> ReadExecutableRequestIdentityAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid operationId,
        CancellationToken cancellationToken)
    {
        if (!await HasRequestKeyTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT immutable_request_hash, preview_fingerprint, request_kind FROM migration_request_keys WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var hash = reader.GetString(0);
        var fingerprint = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (!Enum.TryParse<HorseIdentityMigrationAdmissionKind>(reader.GetString(2), ignoreCase: false,
                out var kind) || !Enum.IsDefined(kind) || !IsSha256Hash(hash)
            || (fingerprint is not null && !IsSha256Hash(fingerprint)))
            throw new InvalidDataException("Migration journal executable request identity is corrupt.");
        return new(hash, kind, fingerprint);
    }

    private static bool SealMatches(HorseIdentityMigrationPendingSeal seal, string immutableRequestHash,
        string fullFingerprint, string sourceSetHash, string backupManifestHash) =>
        string.Equals(seal.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal)
        && string.Equals(seal.FullFingerprint, fullFingerprint, StringComparison.Ordinal)
        && string.Equals(seal.SourceSetHash, sourceSetHash, StringComparison.Ordinal)
        && string.Equals(seal.BackupManifestHash, backupManifestHash, StringComparison.Ordinal);

    private static async Task<HorseIdentityMigrationPendingSeal?> ReadPendingSealAsync(
        SqliteConnection connection, SqliteTransaction? transaction, Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT immutable_request_hash, full_fingerprint, source_set_hash, backup_manifest_hash, sealed_utc FROM migration_pending_seals WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        try
        {
            var requestHash = reader.GetString(0);
            var fingerprint = reader.GetString(1);
            var sourceSetHash = reader.GetString(2);
            var backupManifestHash = reader.GetString(3);
            var sealedAt = DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture);
            if (!IsSha256Hash(requestHash) || !IsSha256Hash(fingerprint) || !IsSha256Hash(sourceSetHash)
                || !IsSha256Hash(backupManifestHash))
                throw new InvalidDataException("Pending migration seal is corrupt.");
            return new(operationId, requestHash, fingerprint, sourceSetHash, backupManifestHash, sealedAt);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("Pending migration seal is corrupt.", exception);
        }
    }

    private async Task<HorseIdentityMigrationExecutionIntent?> ReadExecutionIntentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (!await HasExecutionIntentTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT immutable_request_hash, created_utc, updated_utc, phase, cancellation_source_set_hash, cancellation_diagnostic_reason FROM migration_execution_intents WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        try
        {
            var hash = reader.GetString(0);
            var phase = (HorseIdentityMigrationExecutionPhase)reader.GetInt32(3);
            var cancellationSourceSetHash = reader.IsDBNull(4) ? null : reader.GetString(4);
            HorseIdentityMigrationJournalReason? cancellationDiagnosticReason = reader.IsDBNull(5)
                ? null
                : (HorseIdentityMigrationJournalReason)reader.GetInt32(5);
            if (!IsSha256Hash(hash) || !Enum.IsDefined(phase))
                throw new InvalidDataException("Migration journal execution intent is corrupt.");
            if ((phase == HorseIdentityMigrationExecutionPhase.PreparationCancelled
                    && (cancellationSourceSetHash is null || !IsSha256Hash(cancellationSourceSetHash)))
                || (phase != HorseIdentityMigrationExecutionPhase.PreparationCancelled
                    && cancellationSourceSetHash is not null))
                throw new InvalidDataException("Migration journal cancellation source binding is corrupt.");
            if (cancellationDiagnosticReason is { } diagnostic
                && (diagnostic != HorseIdentityMigrationJournalReason.PreparationFailed
                    || phase != HorseIdentityMigrationExecutionPhase.CancellationRequested))
                throw new InvalidDataException("Migration journal cancellation diagnostic is corrupt.");
            return new(operationId, hash,
                DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture), phase)
            {
                CancellationSourceSetHash = cancellationSourceSetHash,
                CancellationDiagnosticReason = cancellationDiagnosticReason,
            };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException
            or InvalidOperationException or InvalidCastException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("Migration journal execution intent is corrupt.", exception);
        }
    }

    private async Task<HorseIdentityMigrationJournalOperation?> ReadOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT operation_id, request_hash, status, reason, created_utc, updated_utc, results_json, terminal_utc FROM migration_operations WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        try
        {
            var results = JsonSerializer.Deserialize<HorseIdentityMigrationJournalResultItem[]>(reader.GetString(6), JsonOptions)
                ?? [];
            var status = ReadDefinedEnum<HorseIdentityMigrationOperationStatus>(reader.GetInt32(2), "operation status");
            var reason = ReadDefinedEnum<HorseIdentityMigrationJournalReason>(reader.GetInt32(3), "operation reason");
            var reasonPairIsValid = status switch
            {
                HorseIdentityMigrationOperationStatus.Staged or HorseIdentityMigrationOperationStatus.Running
                    or HorseIdentityMigrationOperationStatus.Succeeded => reason == HorseIdentityMigrationJournalReason.None,
                HorseIdentityMigrationOperationStatus.Interrupted or HorseIdentityMigrationOperationStatus.PartialUnverified
                    => reason == HorseIdentityMigrationJournalReason.OperationInterrupted,
                _ => true,
            };
            if (!reasonPairIsValid || IsTerminal(status) != !reader.IsDBNull(7))
                throw new InvalidDataException("Migration journal operation status and reason are inconsistent.");
            if (results.Any(result => !Enum.IsDefined(result.Code)
                || result.Surface is { } surface && !Enum.IsDefined(surface)))
                throw new InvalidDataException("Migration journal result contains an unknown enum value.");
            return new(Guid.ParseExact(reader.GetString(0), "D"), reader.GetString(1),
                status,
                reason,
                DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture), results);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            throw new InvalidDataException("Migration journal operation record is corrupt.");
        }
    }

    private static TEnum ReadDefinedEnum<TEnum>(int value, string fieldName) where TEnum : struct, Enum
    {
        var parsed = (TEnum)Enum.ToObject(typeof(TEnum), value);
        if (!Enum.IsDefined(parsed))
            throw new InvalidDataException($"Migration journal {fieldName} is corrupt.");
        return parsed;
    }

    private async Task<HorseIdentityMigrationPageCheckpoint?> ReadCheckpointAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        string pageIdentity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT surface, table_name, row_count, membership_hash, before_hash, expected_after_hash, state FROM migration_pages WHERE operation_id = $id AND page_identity = $page;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$page", pageIdentity);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(new(ReadDefinedEnum<MigrationStoreSurface>(reader.GetInt32(0), "page surface"), reader.GetString(1), reader.GetInt64(2),
            reader.GetString(4), reader.GetString(5)), reader.GetString(3),
            ReadDefinedEnum<HorseIdentityMigrationPageState>(reader.GetInt32(6), "page state"));
    }

    internal async Task<HorseIdentityMigrationPageCheckpoint?> GetPageCheckpointAsync(
        Guid operationId,
        TypedHorseIdentityPageEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || evidence is null) return null;
        await PrepareReadOnlyDirectoryAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var result = await ReadCheckpointAsync(connection, transaction, operationId,
            BuildPageIdentity(evidence), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<HorseIdentityMigrationPageCheckpoint?> ReadRequiredPageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        string pageIdentity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT surface, table_name, row_count, membership_hash, before_hash, expected_after_hash FROM migration_required_pages WHERE operation_id = $id AND page_identity = $page;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$page", pageIdentity);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(new((MigrationStoreSurface)reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2),
            reader.GetString(4), reader.GetString(5)), reader.GetString(3), HorseIdentityMigrationPageState.Prepared);
    }

    private async Task<IReadOnlyList<HorseIdentityMigrationPageCheckpoint>> ReadRequiredPagesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var pages = new List<HorseIdentityMigrationPageCheckpoint>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT surface, table_name, row_count, membership_hash, before_hash, expected_after_hash FROM migration_required_pages WHERE operation_id = $id ORDER BY page_identity;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            pages.Add(new(new((MigrationStoreSurface)reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2),
                reader.GetString(4), reader.GetString(5)), reader.GetString(3), HorseIdentityMigrationPageState.Prepared));
        return pages;
    }

    private async Task<int> CountRequiredPagesAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid operationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM migration_required_pages WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<long> RequiredEvidenceBytesAsync(SqliteConnection connection,
        SqliteTransaction transaction, Guid operationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(SUM(evidence_bytes), 0) FROM migration_required_pages WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<long> ResultJsonBytesAsync(SqliteConnection connection,
        SqliteTransaction transaction, Guid operationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT length(CAST(results_json AS BLOB)) FROM migration_operations WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private sealed class PageCheckpointComparer : IEqualityComparer<HorseIdentityMigrationPageCheckpoint>
    {
        internal static readonly PageCheckpointComparer Instance = new();
        public bool Equals(HorseIdentityMigrationPageCheckpoint? left, HorseIdentityMigrationPageCheckpoint? right) =>
            left is not null && right is not null && SameCheckpoint(left, right);
        public int GetHashCode(HorseIdentityMigrationPageCheckpoint value) => HashCode.Combine(
            value.Evidence.Surface, value.Evidence.Table, value.Evidence.RowCount,
            value.MembershipHash.ToUpperInvariant(), value.Evidence.BeforeHash.ToUpperInvariant(),
            value.Evidence.ExpectedAfterHash.ToUpperInvariant());
    }

    private async Task<int> CountPagesAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid operationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM migration_pages WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<int> CountVerifiedPagesAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid operationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM migration_pages WHERE operation_id = $id AND state = $verified;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$verified", (int)HorseIdentityMigrationPageState.Verified);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task PurgeEligibleTerminalAsync(SqliteConnection connection, SqliteTransaction transaction,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Cancellation receipts are durable source-bound operator evidence, not ordinary terminal history.
        // Keep the bounded per-operation receipt row indefinitely; never infer or recreate it after pruning.
        command.CommandText = """
            DELETE FROM migration_operations
            WHERE operation_id IN (
                SELECT operation_id FROM migration_operations
                WHERE status IN ($succeeded, $rejected, $failed) AND terminal_utc < $cutoff
                    AND NOT EXISTS (
                        SELECT 1 FROM migration_execution_intents i
                        WHERE i.operation_id = migration_operations.operation_id
                            AND i.phase NOT IN ($verified, $intentFailed)
                    )
                ORDER BY terminal_utc, operation_id LIMIT 16
            );
            """;
        command.Parameters.AddWithValue("$succeeded", (int)HorseIdentityMigrationOperationStatus.Succeeded);
        command.Parameters.AddWithValue("$rejected", (int)HorseIdentityMigrationOperationStatus.Rejected);
        command.Parameters.AddWithValue("$failed", (int)HorseIdentityMigrationOperationStatus.Failed);
        command.Parameters.AddWithValue("$verified", (int)HorseIdentityMigrationExecutionPhase.Verified);
        command.Parameters.AddWithValue("$intentFailed", (int)HorseIdentityMigrationExecutionPhase.Failed);
        command.Parameters.AddWithValue("$cutoff", (now - TerminalRetention).ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasCapacityAsync(SqliteConnection connection, SqliteTransaction transaction,
        long projectedWriteBytes, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA page_count;";
        var pages = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        command.CommandText = "PRAGMA page_size;";
        var pageSize = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        command.CommandText = "PRAGMA max_page_count;";
        var maximumPages = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        var projectedPages = checked(pages + DivideRoundUp(projectedWriteBytes, pageSize) + 2);
        var allFiles = JournalFootprintBytes();
        var otherFiles = Math.Max(0, allFiles - FileLength(_databasePath));
        return projectedPages <= maximumPages
            && WorstCaseRollbackFootprint(projectedPages, pageSize) + otherFiles + _failureReserveBytes <= _maximumBytes;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await PrepareDirectoryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureInitializationMarkerForWriteAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 1,
            ForeignKeys = true,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = FULL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS migration_operations (
                operation_id TEXT PRIMARY KEY NOT NULL,
                request_hash TEXT NOT NULL,
                request_json TEXT NOT NULL,
                status INTEGER NOT NULL,
                reason INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                terminal_utc TEXT NULL,
                results_json TEXT NOT NULL,
                workflow_claimed INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS migration_pages (
                operation_id TEXT NOT NULL,
                page_identity TEXT NOT NULL,
                surface INTEGER NOT NULL,
                table_name TEXT NOT NULL,
                row_count INTEGER NOT NULL,
                membership_hash TEXT NOT NULL,
                before_hash TEXT NOT NULL,
                expected_after_hash TEXT NOT NULL,
                state INTEGER NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY (operation_id, page_identity),
                FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS migration_required_pages (
                operation_id TEXT NOT NULL,
                page_identity TEXT NOT NULL,
                surface INTEGER NOT NULL,
                table_name TEXT NOT NULL,
                row_count INTEGER NOT NULL,
                membership_hash TEXT NOT NULL,
                before_hash TEXT NOT NULL,
                expected_after_hash TEXT NOT NULL,
                evidence_bytes INTEGER NOT NULL,
                PRIMARY KEY (operation_id, page_identity),
                FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS migration_request_keys (
                request_key TEXT PRIMARY KEY NOT NULL,
                immutable_request_hash TEXT NOT NULL,
                preview_fingerprint TEXT NULL,
                request_kind TEXT NOT NULL DEFAULT 'BoundV1',
                operation_id TEXT NOT NULL UNIQUE,
                FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS migration_execution_intents (
                operation_id TEXT PRIMARY KEY NOT NULL,
                immutable_request_hash TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                phase INTEGER NOT NULL,
                cancellation_source_set_hash TEXT NULL,
                cancellation_diagnostic_reason INTEGER NULL,
                FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS migration_pending_seals (
                operation_id TEXT PRIMARY KEY NOT NULL,
                immutable_request_hash TEXT NOT NULL,
                full_fingerprint TEXT NOT NULL,
                source_set_hash TEXT NOT NULL,
                backup_manifest_hash TEXT NOT NULL,
                sealed_utc TEXT NOT NULL,
                FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_migration_execution_intents_single_active
                ON migration_execution_intents ((1))
                WHERE phase NOT IN (9, 10, 12);
            """;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await EnsureRequestKeyPreviewFingerprintColumnAsync(connection, cancellationToken).ConfigureAwait(false);
            await EnsureRequestKeyRequestKindColumnAsync(connection, cancellationToken).ConfigureAwait(false);
            await EnsureExecutionIntentCancellationSourceSetHashColumnAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            await EnsureExecutionIntentCancellationDiagnosticReasonColumnAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            await using (var schemaUpgradeTransaction = connection.BeginTransaction())
            {
                await EnsureSingleActiveIntentIndexAsync(connection, schemaUpgradeTransaction, cancellationToken)
                    .ConfigureAwait(false);
                await schemaUpgradeTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            await ConfigureMaximumPageCountAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureRequestKeyPreviewFingerprintColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE migration_request_keys ADD COLUMN preview_fingerprint TEXT NULL;";
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1
            && exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("preview_fingerprint", StringComparison.OrdinalIgnoreCase))
        {
            // Existing v1 journals are upgraded in place; the nullable column preserves legacy rows.
        }
    }

    private static async Task EnsureRequestKeyRequestKindColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE migration_request_keys ADD COLUMN request_kind TEXT NOT NULL DEFAULT 'BoundV1';";
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1
            && exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("request_kind", StringComparison.OrdinalIgnoreCase))
        {
            // Existing request keys retain their bound-v1 identity when upgraded in place.
        }
    }

    private static async Task EnsurePendingSealTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await HasPendingSealTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            await ExecuteSchemaAsync(connection, transaction, cancellationToken, """
                CREATE TABLE migration_pending_seals (
                    operation_id TEXT PRIMARY KEY NOT NULL,
                    immutable_request_hash TEXT NOT NULL,
                    full_fingerprint TEXT NOT NULL,
                    source_set_hash TEXT NOT NULL,
                    backup_manifest_hash TEXT NOT NULL,
                    sealed_utc TEXT NOT NULL,
                    FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE
                );
                """).ConfigureAwait(false);
        }

        await ValidateTableColumnsAsync(connection, transaction, cancellationToken, "migration_pending_seals",
            ("operation_id", "TEXT", true, 1),
            ("immutable_request_hash", "TEXT", true, 0),
            ("full_fingerprint", "TEXT", true, 0),
            ("source_set_hash", "TEXT", true, 0),
            ("backup_manifest_hash", "TEXT", true, 0),
            ("sealed_utc", "TEXT", true, 0)).ConfigureAwait(false);
        await ValidateForeignKeyAsync(connection, transaction, cancellationToken, "migration_pending_seals",
            "operation_id").ConfigureAwait(false);
    }

    private static async Task EnsureOperationalSchemaUpgradesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ValidateCoreTablesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await EnsureRequestKeyTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await EnsureExecutionIntentTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await EnsureExecutionIntentCancellationSourceSetHashColumnAsync(connection, cancellationToken, transaction)
            .ConfigureAwait(false);
        await EnsureExecutionIntentCancellationDiagnosticReasonColumnAsync(connection, cancellationToken, transaction)
            .ConfigureAwait(false);
        await EnsureRequestKeyPreviewFingerprintColumnAsync(connection, cancellationToken, transaction)
            .ConfigureAwait(false);
        await EnsureRequestKeyRequestKindColumnAsync(connection, cancellationToken, transaction)
            .ConfigureAwait(false);
        await ValidateTableColumnsAsync(connection, transaction, cancellationToken, "migration_request_keys",
            ("request_key", "TEXT", true, 1),
            ("immutable_request_hash", "TEXT", true, 0),
            ("preview_fingerprint", "TEXT", false, 0),
            ("request_kind", "TEXT", true, 0),
            ("operation_id", "TEXT", true, 0)).ConfigureAwait(false);
        await ValidateUniqueColumnIndexAsync(connection, transaction, cancellationToken, "migration_request_keys",
            "operation_id").ConfigureAwait(false);
        await ValidateForeignKeyAsync(connection, transaction, cancellationToken, "migration_request_keys",
            "operation_id").ConfigureAwait(false);
        await ValidateTableColumnsAsync(connection, transaction, cancellationToken, "migration_execution_intents",
            ("operation_id", "TEXT", true, 1),
            ("immutable_request_hash", "TEXT", true, 0),
            ("created_utc", "TEXT", true, 0),
            ("updated_utc", "TEXT", true, 0),
            ("phase", "INTEGER", true, 0),
            ("cancellation_source_set_hash", "TEXT", false, 0),
            ("cancellation_diagnostic_reason", "INTEGER", false, 0)).ConfigureAwait(false);
        await ValidateForeignKeyAsync(connection, transaction, cancellationToken, "migration_execution_intents",
            "operation_id").ConfigureAwait(false);
        await EnsurePendingSealTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await EnsureSingleActiveIntentIndexAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureRequestKeyTableAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (await HasRequestKeyTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            await ValidateTableColumnsAsync(connection, transaction, cancellationToken, "migration_request_keys",
                [
                    ("request_key", "TEXT", true, 1),
                    ("immutable_request_hash", "TEXT", true, 0),
                    ("operation_id", "TEXT", true, 0),
                    ("preview_fingerprint", "TEXT", false, 0),
                    ("request_kind", "TEXT", true, 0),
                ], ["preview_fingerprint", "request_kind"])
                .ConfigureAwait(false);
            return;
        }

        await ExecuteSchemaAsync(connection, transaction, cancellationToken, """
            CREATE TABLE migration_request_keys (
                request_key TEXT PRIMARY KEY NOT NULL,
                immutable_request_hash TEXT NOT NULL,
                preview_fingerprint TEXT NULL,
                request_kind TEXT NOT NULL DEFAULT 'BoundV1',
                operation_id TEXT NOT NULL UNIQUE,
                FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE
            );
            """).ConfigureAwait(false);
    }

    private static async Task EnsureExecutionIntentTableAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        if (await HasExecutionIntentTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return;

        await ExecuteSchemaAsync(connection, transaction, cancellationToken, """
            CREATE TABLE migration_execution_intents (
                operation_id TEXT PRIMARY KEY NOT NULL,
                immutable_request_hash TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                phase INTEGER NOT NULL,
                cancellation_source_set_hash TEXT NULL,
                cancellation_diagnostic_reason INTEGER NULL,
                FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE
            );
            """).ConfigureAwait(false);
    }

    private static async Task EnsureExecutionIntentCancellationSourceSetHashColumnAsync(
        SqliteConnection connection, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE migration_execution_intents ADD COLUMN cancellation_source_set_hash TEXT NULL;";
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1
            && exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("cancellation_source_set_hash", StringComparison.OrdinalIgnoreCase))
        {
            // Historical intents gain a nullable receipt binding without rewriting their rows.
        }
    }

    private static async Task EnsureExecutionIntentCancellationDiagnosticReasonColumnAsync(
        SqliteConnection connection, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE migration_execution_intents ADD COLUMN cancellation_diagnostic_reason INTEGER NULL;";
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1
            && exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("cancellation_diagnostic_reason", StringComparison.OrdinalIgnoreCase))
        {
            // Historical intents retain a null safe diagnostic when upgraded in place.
        }
    }

    private static async Task EnsureSingleActiveIntentIndexAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        const string indexName = "IX_migration_execution_intents_single_active";
        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $name;";
        lookup.Parameters.AddWithValue("$name", indexName);
        var indexSql = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var existingSql = indexSql is string text ? text : null;
        const string expectedSql = "CREATE UNIQUE INDEX IX_migration_execution_intents_single_active " +
            "ON migration_execution_intents ((1)) WHERE phase NOT IN (9, 10, 12)";
        const string previousSql = "CREATE UNIQUE INDEX IX_migration_execution_intents_single_active " +
            "ON migration_execution_intents ((1)) WHERE phase NOT IN (9, 10)";
        if (existingSql is not null
            && string.Equals(NormalizeSchemaSql(existingSql), NormalizeSchemaSql(previousSql), StringComparison.Ordinal))
        {
            await ExecuteSchemaAsync(connection, transaction, cancellationToken,
                "DROP INDEX IX_migration_execution_intents_single_active;").ConfigureAwait(false);
            existingSql = null;
        }

        if (existingSql is null)
        {
            await ExecuteSchemaAsync(connection, transaction, cancellationToken, """
                CREATE UNIQUE INDEX IX_migration_execution_intents_single_active
                    ON migration_execution_intents ((1))
                    WHERE phase NOT IN (9, 10, 12);
                """).ConfigureAwait(false);
            existingSql = expectedSql;
        }

        if (!string.Equals(NormalizeSchemaSql(existingSql), NormalizeSchemaSql(expectedSql),
                StringComparison.Ordinal))
            throw new InvalidDataException("Migration journal active-intent index is corrupt.");

        await using var properties = connection.CreateCommand();
        properties.Transaction = transaction;
        properties.CommandText = "PRAGMA index_list('migration_execution_intents');";
        await using var reader = await properties.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var found = false;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(reader.GetString(1), indexName, StringComparison.Ordinal)) continue;
            found = reader.GetInt32(2) == 1 && reader.GetInt32(4) == 1;
            break;
        }
        if (!found) throw new InvalidDataException("Migration journal active-intent index is corrupt.");
    }

    private static async Task ValidateCoreTablesAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ValidateTableColumnsAsync(connection, transaction, cancellationToken, "migration_operations",
            ("operation_id", "TEXT", true, 1), ("request_hash", "TEXT", true, 0),
            ("request_json", "TEXT", true, 0), ("status", "INTEGER", true, 0), ("reason", "INTEGER", true, 0),
            ("created_utc", "TEXT", true, 0), ("updated_utc", "TEXT", true, 0),
            ("terminal_utc", "TEXT", false, 0), ("results_json", "TEXT", true, 0),
            ("workflow_claimed", "INTEGER", true, 0)).ConfigureAwait(false);
        await ValidateTableColumnsAsync(connection, transaction, cancellationToken, "migration_pages",
            ("operation_id", "TEXT", true, 1), ("page_identity", "TEXT", true, 2),
            ("surface", "INTEGER", true, 0), ("table_name", "TEXT", true, 0),
            ("row_count", "INTEGER", true, 0), ("membership_hash", "TEXT", true, 0),
            ("before_hash", "TEXT", true, 0), ("expected_after_hash", "TEXT", true, 0),
            ("state", "INTEGER", true, 0), ("updated_utc", "TEXT", true, 0)).ConfigureAwait(false);
        await ValidateTableColumnsAsync(connection, transaction, cancellationToken, "migration_required_pages",
            ("operation_id", "TEXT", true, 1), ("page_identity", "TEXT", true, 2),
            ("surface", "INTEGER", true, 0), ("table_name", "TEXT", true, 0),
            ("row_count", "INTEGER", true, 0), ("membership_hash", "TEXT", true, 0),
            ("before_hash", "TEXT", true, 0), ("expected_after_hash", "TEXT", true, 0),
            ("evidence_bytes", "INTEGER", true, 0)).ConfigureAwait(false);
        await ValidateForeignKeyAsync(connection, transaction, cancellationToken, "migration_pages",
            "operation_id").ConfigureAwait(false);
        await ValidateForeignKeyAsync(connection, transaction, cancellationToken, "migration_required_pages",
            "operation_id").ConfigureAwait(false);
    }

    private static async Task ValidateForeignKeyAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken, string table, string fromColumn)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA foreign_key_list('{table}');";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(2), "migration_operations", StringComparison.Ordinal)
                && string.Equals(reader.GetString(3), fromColumn, StringComparison.Ordinal)
                && string.Equals(reader.GetString(4), "operation_id", StringComparison.Ordinal)
                && string.Equals(reader.GetString(6), "CASCADE", StringComparison.OrdinalIgnoreCase))
                return;
        }
        throw new InvalidDataException($"Migration journal table {table} is corrupt.");
    }

    private static async Task ValidateUniqueColumnIndexAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken, string table, string column)
    {
        await using var indexes = connection.CreateCommand();
        indexes.Transaction = transaction;
        indexes.CommandText = $"PRAGMA index_list('{table}');";
        await using var indexReader = await indexes.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var indexNames = new List<string>();
        while (await indexReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (indexReader.GetInt32(2) == 1 && indexReader.GetInt32(4) == 0)
                indexNames.Add(indexReader.GetString(1));
        await indexReader.DisposeAsync().ConfigureAwait(false);

        foreach (var indexName in indexNames)
        {
            await using var info = connection.CreateCommand();
            info.Transaction = transaction;
            info.CommandText = $"PRAGMA index_info('{indexName.Replace("'", "''", StringComparison.Ordinal)}');";
            await using var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var columns = new List<string>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                columns.Add(reader.GetString(2));
            if (columns.Count == 1 && string.Equals(columns[0], column, StringComparison.Ordinal)) return;
        }
        throw new InvalidDataException($"Migration journal table {table} is corrupt.");
    }

    private static Task ValidateTableColumnsAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken, string table,
        params (string Name, string Type, bool NotNull, int PrimaryKey)[] expected) =>
        ValidateTableColumnsCoreAsync(connection, transaction, cancellationToken, table, expected, null);

    private static Task ValidateTableColumnsAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken, string table,
        (string Name, string Type, bool NotNull, int PrimaryKey)[] expected, string[] allowMissing) =>
        ValidateTableColumnsCoreAsync(connection, transaction, cancellationToken, table, expected, allowMissing);

    private static async Task ValidateTableColumnsCoreAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken, string table,
        (string Name, string Type, bool NotNull, int PrimaryKey)[] expected,
        string[]? allowMissing)
    {
        var permitted = expected.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
        var required = expected.Select(column => column.Name).Except(allowMissing ?? [], StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var actual = new Dictionary<string, (string Type, bool NotNull, int PrimaryKey)>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA table_info('{table}');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                actual.Add(reader.GetString(1), (reader.GetString(2), reader.GetInt32(3) != 0, reader.GetInt32(5)));
        }

        if (!required.IsSubsetOf(actual.Keys) || actual.Keys.Any(name => !permitted.Contains(name)))
            throw new InvalidDataException($"Migration journal table {table} is corrupt.");
        foreach (var column in expected)
        {
            if (!actual.TryGetValue(column.Name, out var value)) continue;
            if (!string.Equals(value.Type, column.Type, StringComparison.OrdinalIgnoreCase)
                || value.NotNull != column.NotNull || value.PrimaryKey != column.PrimaryKey)
                throw new InvalidDataException($"Migration journal table {table} is corrupt.");
        }
    }

    private static async Task ExecuteSchemaAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeSchemaSql(string sql) => string.Concat(sql.Where(character => !char.IsWhiteSpace(character)))
        .ToUpperInvariant();

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        await PrepareDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var markerExists = File.Exists(_initializationMarkerPath);
        var databaseExists = File.Exists(_databasePath);
        if (!markerExists && !databaseExists)
        {
            await using var initialized = await OpenAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!markerExists || !databaseExists)
            throw new InvalidDataException("Migration journal initialization state is incomplete.");

        await ValidateInitializationMarkerAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenExistingAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializationMarkerForWriteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var markerExists = File.Exists(_initializationMarkerPath);
        var databaseExists = File.Exists(_databasePath);
        if (markerExists)
        {
            if (!databaseExists)
                throw new InvalidDataException("Initialized migration journal is missing.");
            await ValidateInitializationMarkerAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (databaseExists)
            throw new InvalidDataException("Migration journal initialization marker is missing.");

        ThrowIfFileReparsePoint(_initializationMarkerPath);
        await using var marker = new FileStream(_initializationMarkerPath, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 256, FileOptions.WriteThrough);
        var content = Encoding.ASCII.GetBytes(InitializationMarkerContent);
        await marker.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        marker.Flush(flushToDisk: true);
    }

    private async Task ValidateInitializationMarkerAsync(CancellationToken cancellationToken)
    {
        ThrowIfFileReparsePoint(_initializationMarkerPath);
        var info = new FileInfo(_initializationMarkerPath);
        if (!info.Exists || info.Length != Encoding.ASCII.GetByteCount(InitializationMarkerContent))
            throw new InvalidDataException("Migration journal initialization marker is corrupt.");
        var content = await File.ReadAllTextAsync(_initializationMarkerPath, Encoding.ASCII, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(content, InitializationMarkerContent, StringComparison.Ordinal))
            throw new InvalidDataException("Migration journal initialization marker is corrupt.");
    }

    private async Task<SqliteConnection> OpenExistingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PrepareReadOnlyDirectoryAsync(cancellationToken).ConfigureAwait(false);
        if (!File.Exists(_databasePath))
            throw new InvalidDataException("Initialized migration journal is missing.");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 1,
            ForeignKeys = true,
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            foreach (var query in new[]
            {
                "SELECT operation_id, request_hash, request_json, status, reason, created_utc, updated_utc, terminal_utc, results_json, workflow_claimed FROM migration_operations LIMIT 0;",
                "SELECT operation_id, page_identity, surface, table_name, row_count, membership_hash, before_hash, expected_after_hash, state, updated_utc FROM migration_pages LIMIT 0;",
                "SELECT operation_id, page_identity, surface, table_name, row_count, membership_hash, before_hash, expected_after_hash, evidence_bytes FROM migration_required_pages LIMIT 0;",
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = query;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            }

            if (await HasRequestKeyTableAsync(connection, null, cancellationToken).ConfigureAwait(false))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT request_key, immutable_request_hash, operation_id FROM migration_request_keys LIMIT 0;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            }
            if (await HasExecutionIntentTableAsync(connection, null, cancellationToken).ConfigureAwait(false))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT operation_id, immutable_request_hash, created_utc, updated_utc, phase FROM migration_execution_intents LIMIT 0;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            }

            await ValidateInitializationMarkerAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SqliteConnection> OpenReadOnlyAsync(CancellationToken cancellationToken)
    {
        await PrepareReadOnlyDirectoryAsync(cancellationToken).ConfigureAwait(false);
        await ValidateInitializationMarkerAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ConfigureMaximumPageCountAsync(SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA page_size;";
        var pageSize = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (pageSize is < 512 or > 65_536 || (pageSize & (pageSize - 1)) != 0)
            throw new InvalidDataException("Migration journal has an unsupported SQLite page size.");

        var allFiles = JournalFootprintBytes();
        var otherFiles = Math.Max(0, allFiles - FileLength(_databasePath));
        var low = ComputeMaximumPageCount(_maximumBytes, _failureReserveBytes, otherFiles, pageSize);
        if (low == 0)
            throw new InvalidDataException("Migration journal capacity is too small for its SQLite safety bound.");

        command.CommandText = $"PRAGMA max_page_count = {low};";
        var applied = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (applied > low)
            throw new InvalidDataException("Migration journal could not apply its SQLite page ceiling.");
    }

    private static long WorstCaseRollbackFootprint(long pages, long pageSize)
    {
        if (pages == 0) return 0;
        // A rollback record contains one original page plus its 8-byte page number/checksum.
        // Bound repeated rollback headers by one sector per page, plus the initial header sector.
        return checked(pages * pageSize
            + pages * (pageSize + RollbackJournalRecordOverheadBytes)
            + (pages + 1) * MaximumSqliteSectorSizeBytes
            + RollbackJournalHeaderBytes);
    }

    internal static long ComputeMaximumPageCount(long maximumBytes, long reserveBytes, long otherFileBytes,
        long pageSize)
    {
        var budget = maximumBytes - reserveBytes - otherFileBytes;
        long low = 0;
        long high = Math.Min(uint.MaxValue - 1L, Math.Max(0, budget / pageSize));
        while (low < high)
        {
            var candidate = low + (high - low + 1) / 2;
            if (WorstCaseRollbackFootprint(candidate, pageSize) <= budget) low = candidate;
            else high = candidate - 1;
        }
        return low;
    }

    private static long DivideRoundUp(long value, long divisor) => value == 0 ? 0 : (value + divisor - 1) / divisor;

    private async Task PrepareDirectoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfReparsePointInExistingPath(_directory);
        Directory.CreateDirectory(_directory);
        ThrowIfReparsePointInExistingPath(_directory);
        if (_protectedPaths.Contains(Path.GetFullPath(_databasePath))
            || _protectedPaths.Contains(Path.GetFullPath(_lockPath))
            || _protectedPaths.Contains(Path.GetFullPath(_initializationMarkerPath)))
            throw new InvalidOperationException("Journal path conflicts with a configured store.");
        ThrowIfFileReparsePoint(_databasePath);
        ThrowIfFileReparsePoint(_lockPath);
        ThrowIfFileReparsePoint(_initializationMarkerPath);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private Task PrepareReadOnlyDirectoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfReparsePointInExistingPath(_directory);
        ThrowIfFileReparsePoint(_databasePath);
        ThrowIfFileReparsePoint(_initializationMarkerPath);
        if (_protectedPaths.Contains(Path.GetFullPath(_databasePath))
            || _protectedPaths.Contains(Path.GetFullPath(_initializationMarkerPath)))
            throw new InvalidOperationException("Journal path conflicts with a configured store.");
        return Task.CompletedTask;
    }

    private long JournalFootprintBytes() => FileLength(_databasePath) + FileLength(_databasePath + "-journal")
        + FileLength(_databasePath + "-wal") + FileLength(_databasePath + "-shm") + FileLength(_lockPath);

    private static long FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void ThrowIfReparsePointInExistingPath(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Journal directory cannot contain a reparse point.");
            current = current.Parent;
        }
    }

    private static void ThrowIfFileReparsePoint(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Journal file cannot be a reparse point.");
    }

    private static bool ValidateCheckpoint(HorseIdentityMigrationPageCheckpoint checkpoint)
    {
        var evidence = checkpoint.Evidence;
        return evidence is not null && Enum.IsDefined(evidence.Surface)
            && Enum.IsDefined(checkpoint.State) && evidence.RowCount >= 0
            && SafeToken(evidence.Table, 128)
            && IsSha256(evidence.BeforeHash) && IsSha256(evidence.ExpectedAfterHash)
            && IsSha256(checkpoint.MembershipHash)
            && (evidence.FirstKey is null || SafeToken(evidence.FirstKey, 128))
            && (evidence.LastKey is null || SafeToken(evidence.LastKey, 128));
    }

    private static bool SafeToken(string value, int maximumLength) => value.Length > 0 && value.Length <= maximumLength
        && !value.Contains("://", StringComparison.Ordinal)
        && !value.Contains('\\') && !value.Contains('/') && !Path.IsPathRooted(value);

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character => Uri.IsHexDigit(character));

    private static string BuildPageIdentity(TypedHorseIdentityPageEvidence evidence)
    {
        var identity = string.Join('\n', (int)evidence.Surface, evidence.Table,
            evidence.FirstKey ?? string.Empty, evidence.LastKey ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static bool SameCheckpoint(HorseIdentityMigrationPageCheckpoint left,
        HorseIdentityMigrationPageCheckpoint right) => left.Evidence.Surface == right.Evidence.Surface
        && left.Evidence.Table == right.Evidence.Table && left.Evidence.RowCount == right.Evidence.RowCount
        && left.MembershipHash.Equals(right.MembershipHash, StringComparison.OrdinalIgnoreCase)
        && left.Evidence.BeforeHash.Equals(right.Evidence.BeforeHash, StringComparison.OrdinalIgnoreCase)
        && left.Evidence.ExpectedAfterHash.Equals(right.Evidence.ExpectedAfterHash, StringComparison.OrdinalIgnoreCase);

    private static bool IsMutableStatus(HorseIdentityMigrationOperationStatus status) =>
        status is HorseIdentityMigrationOperationStatus.Staged or HorseIdentityMigrationOperationStatus.Running;

    private static bool IsTerminal(HorseIdentityMigrationOperationStatus status) =>
        status is HorseIdentityMigrationOperationStatus.Succeeded or HorseIdentityMigrationOperationStatus.Rejected
            or HorseIdentityMigrationOperationStatus.Failed;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal sealed class HorseIdentityMigrationExecutionLease : IAsyncDisposable
    {
        private readonly HorseIdentityMigrationJournal _journal;
        private readonly Guid _operationId;
        private FileStream? _lockHandle;
        private bool _completed;

        internal HorseIdentityMigrationExecutionLease(HorseIdentityMigrationJournal journal, Guid operationId,
            FileStream lockHandle)
        {
            _journal = journal;
            _operationId = operationId;
            _lockHandle = lockHandle;
        }

        internal Guid OperationId => _operationId;

        internal async Task CompleteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await _journal.CompleteAsync(_operationId, now, cancellationToken).ConfigureAwait(false);
            _completed = true;
        }

        internal async Task MarkPartialAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await _journal.MarkPartialAsync(_operationId, now, cancellationToken).ConfigureAwait(false);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed && _lockHandle is not null)
            {
                try
                {
                    await _journal.MarkPartialAsync(_operationId, DateTimeOffset.UtcNow, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _lockHandle.Dispose();
                    _lockHandle = null;
                }
            }
            else
            {
                _lockHandle?.Dispose();
                _lockHandle = null;
            }
        }
    }
}
