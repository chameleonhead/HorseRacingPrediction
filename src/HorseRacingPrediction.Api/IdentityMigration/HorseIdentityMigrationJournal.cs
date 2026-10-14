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
    private const int MaximumSqliteSectorSizeBytes = 64 * 1024;
    private const int RollbackJournalRecordOverheadBytes = 8;
    private const int RollbackJournalHeaderBytes = 24;
    private const string JournalFileName = "horse-identity-migration-journal.db";
    private const string LockFileName = "horse-identity-migration-operation.lock";
    private const string InitializationMarkerFileName = "horse-identity-migration-initialized.marker";
    private const string InitializationMarkerContent = "horse-identity-migration-journal-v1";
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
                    invalid.Parameters.AddWithValue("$maxReason", (int)HorseIdentityMigrationJournalReason.OperationInterrupted);
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

    internal async Task<HorseIdentityMigrationJournalOutcome> StageAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || manifest is null)
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

        var existing = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            var existingRequest = await ReadRequestHashAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(existingRequest, requestHash, StringComparison.Ordinal)
                ? new(existing, HorseIdentityMigrationJournalReason.None)
                : new(existing, HorseIdentityMigrationJournalReason.RequestConflict);
        }

        if (!await HasCapacityAsync(connection, transaction, requestBytes,
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

        var staged = await ReadOperationAsync(connection, transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(staged, HorseIdentityMigrationJournalReason.None);
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

    private async Task CompleteAsync(Guid operationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        if (!await HasCompleteVerifiedEvidenceAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false))
            throw new InvalidOperationException("All typed migration pages must be independently verified before success.");
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
        command.CommandText = """
            DELETE FROM migration_operations
            WHERE operation_id IN (
                SELECT operation_id FROM migration_operations
                WHERE status IN ($succeeded, $rejected, $failed) AND terminal_utc < $cutoff
                ORDER BY terminal_utc, operation_id LIMIT 16
            );
            """;
        command.Parameters.AddWithValue("$succeeded", (int)HorseIdentityMigrationOperationStatus.Succeeded);
        command.Parameters.AddWithValue("$rejected", (int)HorseIdentityMigrationOperationStatus.Rejected);
        command.Parameters.AddWithValue("$failed", (int)HorseIdentityMigrationOperationStatus.Failed);
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
            """;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureMaximumPageCountAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

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
