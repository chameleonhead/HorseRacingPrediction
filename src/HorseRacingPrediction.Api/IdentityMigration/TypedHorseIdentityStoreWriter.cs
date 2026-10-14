using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum TypedHorseIdentityExecutionStatus
{
    Completed,
    NotOwned,
    RejectedBeforeWrite,
    PartialUnverified,
}

internal sealed record TypedHorseIdentityExecutionResult(
    TypedHorseIdentityExecutionStatus Status,
    HorseIdentityMigrationJournalReason Reason,
    int VerifiedPageCount,
    bool CanWrite);

/// <summary>Owner-bound, fixture-only execution path for explicitly typed physical identity roles.</summary>
internal sealed class TypedHorseIdentityStoreWriter(
    TypedHorseIdentityStoreReaders readers,
    TypedHorseIdentityStoreEngine engine,
    HorseIdentityMigrationJournal journal,
    Func<CancellationToken, Task>? beforeFirstWrite = null,
    Func<CancellationToken, Task>? afterPageCommit = null,
    TimeProvider? timeProvider = null)
{
    private const int PageSize = 128;
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureRecordTimeout = TimeSpan.FromSeconds(5);

    internal async Task<TypedHorseIdentityExecutionResult> ExecuteAsync(
        Guid operationId,
        HorseIdentityMigrationManifest? manifest,
        CancellationToken cancellationToken)
    {
        var validation = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        if (!validation.IsValid)
            return new(TypedHorseIdentityExecutionStatus.RejectedBeforeWrite,
                HorseIdentityMigrationJournalReason.RequestConflict, 0, CanWrite: false);

        var executionTimeProvider = timeProvider ?? TimeProvider.System;
        var executionBudget = new TypedHorseIdentityExecutionBudget(executionTimeProvider);
        using var deadlineSource = new CancellationTokenSource(ExecutionTimeout, executionTimeProvider);
        using var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadlineSource.Token);
        var operationToken = operationSource.Token;
        executionBudget.EnsureWithinBounds(operationToken);
        var lease = await journal.TryAcquireExecutionAsync(operationId, executionTimeProvider.GetUtcNow(), operationToken)
            .ConfigureAwait(false);
        if (lease is null)
            return new(TypedHorseIdentityExecutionStatus.NotOwned,
                HorseIdentityMigrationJournalReason.OwnershipBusy, 0, CanWrite: false);

        var verifiedPages = 0;
        try
        {
            await using (lease.ConfigureAwait(false))
            {
                executionBudget.EnsureWithinBounds(operationToken);
                var operation = await journal.GetStatusAsync(lease.OperationId, operationToken).ConfigureAwait(false);
                var requestJson = JsonSerializer.Serialize(validation.Mappings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
                if (operation is null || operation.Status != HorseIdentityMigrationOperationStatus.Running
                    || !FixedHashEquals(operation.RequestHash, requestHash))
                {
                    await lease.MarkPartialAsync(executionTimeProvider.GetUtcNow(), operationToken).ConfigureAwait(false);
                    return new(TypedHorseIdentityExecutionStatus.RejectedBeforeWrite,
                        HorseIdentityMigrationJournalReason.RequestConflict, verifiedPages, CanWrite: false);
                }
                var snapshot = await engine.InspectExecutionInputsAsync(manifest, operationToken)
                    .ConfigureAwait(false);
                executionBudget.EnsureWithinBounds(operationToken);
                var blocker = FindPreflightBlocker(snapshot);
                if (blocker is not null)
                {
                    await lease.MarkPartialAsync(executionTimeProvider.GetUtcNow(), operationToken).ConfigureAwait(false);
                    return new(TypedHorseIdentityExecutionStatus.RejectedBeforeWrite, blocker.Value,
                        verifiedPages, CanWrite: false);
                }
                executionBudget.AddPreviouslyScanned(snapshot.TypedReferences!.InspectedRows,
                    snapshot.TypedReferences.InspectedBytes, operationToken);

                var referenceReader = readers.CreateReferenceReaderForExecution();
                var configuredConnections = new Dictionary<MigrationStoreSurface, SqliteConnection>();
                try
                {
                    foreach (var schema in snapshot.Schemas.Where(schema => schema.Status == MigrationStoreReadStatus.Observed))
                    {
                        executionBudget.EnsureWithinBounds(operationToken);
                        var connection = await readers.OpenConfiguredStoreForExecutionAsync(schema.Surface, operationToken)
                            .ConfigureAwait(false);
                        if (connection is null)
                            throw new InvalidOperationException("A configured store was not available for execution.");
                        configuredConnections.Add(schema.Surface, connection);
                    }

                    var checkpoints = new List<HorseIdentityMigrationPageCheckpoint>();
                    var preparedBySurface = new Dictionary<MigrationStoreSurface,
                        IReadOnlyList<TypedHorseIdentityExecutionPage>>();
                    foreach (var pair in configuredConnections)
                    {
                        executionBudget.EnsureWithinBounds(operationToken);
                        var prepared = await referenceReader.PrepareCanonicalExecutionPagesAsync(pair.Key,
                            pair.Value, validation.Mappings, executionBudget, operationToken).ConfigureAwait(false);
                        preparedBySurface.Add(pair.Key, prepared);
                        checkpoints.AddRange(prepared.Select(page => new HorseIdentityMigrationPageCheckpoint(
                            page.Evidence, page.MembershipHash, HorseIdentityMigrationPageState.Prepared)));
                    }
                    if (checkpoints.Count == 0)
                        throw new InvalidDataException("No complete physical page set was produced.");

                    var registration = await journal.RegisterRequiredPagesAsync(lease.OperationId, checkpoints,
                        executionTimeProvider.GetUtcNow(), operationToken).ConfigureAwait(false);
                    if (!registration.Succeeded)
                        throw new InvalidDataException("Required page registration was rejected.");
                    foreach (var page in checkpoints)
                    {
                        executionBudget.EnsureWithinBounds(operationToken);
                        var checkpoint = await journal.RecordPageCheckpointAsync(lease.OperationId, page,
                            executionTimeProvider.GetUtcNow(), operationToken).ConfigureAwait(false);
                        if (!checkpoint.Succeeded)
                            throw new InvalidDataException("Prepared page checkpoint was rejected.");
                    }

                    if (beforeFirstWrite is not null)
                        await beforeFirstWrite(operationToken).ConfigureAwait(false);
                    executionBudget.EnsureWithinBounds(operationToken);

                    // A single bounded whole-plan freshness pass prevents a later stale page from being
                    // discovered only after an earlier page has already changed a business row.
                    foreach (var (surface, surfacePages) in preparedBySurface)
                    {
                        var connection = configuredConnections[surface];
                        foreach (var page in surfacePages)
                        {
                            executionBudget.EnsureWithinBounds(operationToken);
                            var columns = await ReadColumnsAsync(connection, page.Evidence.Table, operationToken)
                                .ConfigureAwait(false);
                            var current = await referenceReader.ReadAndTransformExecutionPageAsync(connection,
                                page.Evidence, validation.Mappings, operationToken, executionBudget).ConfigureAwait(false);
                            var membership = TypedHorseIdentityReferenceReader.ComputeMembershipHash(
                                current.Select(row => row.RowId).ToArray());
                            var (before, expected) = TypedHorseIdentityReferenceReader.ComputePhysicalPageHashes(columns, current);
                            if (!FixedHashEquals(membership, page.MembershipHash)
                                || !FixedHashEquals(before, page.Evidence.BeforeHash)
                                || !FixedHashEquals(expected, page.Evidence.ExpectedAfterHash))
                                throw new InvalidDataException("Prepared execution plan is stale before the first write.");
                        }
                    }

                    foreach (var (surface, surfacePages) in preparedBySurface)
                    {
                        var connection = configuredConnections[surface];
                        foreach (var page in surfacePages)
                        {
                            executionBudget.EnsureWithinBounds(operationToken);
                            var columns = await ReadColumnsAsync(connection, page.Evidence.Table, operationToken)
                                .ConfigureAwait(false);
                            var current = await referenceReader.ReadAndTransformExecutionPageAsync(connection,
                                page.Evidence, validation.Mappings, operationToken, executionBudget).ConfigureAwait(false);
                            var membershipHash = TypedHorseIdentityReferenceReader.ComputeMembershipHash(
                                current.Select(row => row.RowId).ToArray());
                            var (beforeHash, _) = TypedHorseIdentityReferenceReader.ComputePhysicalPageHashes(columns, current);
                            if (!FixedHashEquals(membershipHash, page.MembershipHash)
                                || !FixedHashEquals(beforeHash, page.Evidence.BeforeHash))
                                throw new InvalidDataException("Execution page is stale before write.");

                            executionBudget.EnsureWithinBounds(operationToken);
                            await using (var transaction = connection.BeginTransaction())
                            {
                                foreach (var row in current)
                                    await UpdateChangedValuesAsync(connection, transaction, page.Evidence.Table,
                                        columns, row, operationToken, executionBudget).ConfigureAwait(false);
                                executionBudget.EnsureWithinBounds(operationToken);
                                await transaction.CommitAsync(operationToken).ConfigureAwait(false);
                            }

                            if (afterPageCommit is not null)
                                await afterPageCommit(operationToken).ConfigureAwait(false);
                            executionBudget.EnsureWithinBounds(operationToken);

                            var committed = new HorseIdentityMigrationPageCheckpoint(page.Evidence,
                                page.MembershipHash, HorseIdentityMigrationPageState.CommittedUnverified);
                            var committedOutcome = await journal.RecordPageCheckpointAsync(lease.OperationId,
                                committed, executionTimeProvider.GetUtcNow(), operationToken).ConfigureAwait(false);
                            if (!committedOutcome.Succeeded)
                                throw new InvalidDataException("Committed page checkpoint was rejected.");

                            var reread = await referenceReader.ReadAndTransformExecutionPageAsync(connection,
                                page.Evidence, validation.Mappings, operationToken, executionBudget).ConfigureAwait(false);
                            var actualMembershipHash = TypedHorseIdentityReferenceReader.ComputeMembershipHash(
                                reread.Select(row => row.RowId).ToArray());
                            var (actualHash, expectedHash) = TypedHorseIdentityReferenceReader.ComputePhysicalPageHashes(columns, reread);
                            if (!FixedHashEquals(actualMembershipHash, page.MembershipHash)
                                || !FixedHashEquals(actualHash, page.Evidence.ExpectedAfterHash)
                                || !FixedHashEquals(expectedHash, page.Evidence.ExpectedAfterHash))
                                throw new InvalidDataException("Committed page did not match typed expected values.");

                            var verified = new HorseIdentityMigrationPageCheckpoint(page.Evidence,
                                page.MembershipHash, HorseIdentityMigrationPageState.Verified);
                            var verifiedOutcome = await journal.RecordPageCheckpointAsync(lease.OperationId,
                                verified, executionTimeProvider.GetUtcNow(), operationToken).ConfigureAwait(false);
                            if (!verifiedOutcome.Succeeded)
                                throw new InvalidDataException("Verified page checkpoint was rejected.");
                            verifiedPages++;
                        }
                    }
                    executionBudget.EnsureWithinBounds(operationToken);
                    await lease.CompleteAsync(executionTimeProvider.GetUtcNow(), operationToken).ConfigureAwait(false);
                    return new(TypedHorseIdentityExecutionStatus.Completed,
                        HorseIdentityMigrationJournalReason.None, verifiedPages, CanWrite: true);
                }
                finally
                {
                    foreach (var connection in configuredConnections.Values)
                        await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryMarkPartialAsync(lease, executionTimeProvider).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await TryMarkPartialAsync(lease, executionTimeProvider).ConfigureAwait(false);
            return new(TypedHorseIdentityExecutionStatus.PartialUnverified,
                HorseIdentityMigrationJournalReason.OperationInterrupted, verifiedPages, CanWrite: false);
        }
    }

    private async Task TryMarkPartialAsync(
        HorseIdentityMigrationJournal.HorseIdentityMigrationExecutionLease lease,
        TimeProvider clock)
    {
        using var cleanup = new CancellationTokenSource(FailureRecordTimeout);
        try
        {
            await lease.MarkPartialAsync(clock.GetUtcNow(), cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    internal static HorseIdentityMigrationJournalReason? FindPreflightBlocker(MigrationStoreReadSnapshot snapshot)
    {
        if (snapshot.Schemas.Any(schema => schema.Status == MigrationStoreReadStatus.Unavailable))
            return HorseIdentityMigrationJournalReason.OperationInterrupted;
        if (snapshot.Schemas.Any(schema => schema.Status is not (MigrationStoreReadStatus.Observed
                or MigrationStoreReadStatus.NotConfigured)))
            return HorseIdentityMigrationJournalReason.OperationInterrupted;
        if (snapshot.Roots.Any(root => root.HasTargetCollision)
            || snapshot.TypedReferences?.HasCollision == true)
            return HorseIdentityMigrationJournalReason.RequestConflict;
        if (snapshot.Roots.Any(root => root.SourceStatus != MigrationRootStatus.Present)
            || snapshot.TypedReferenceClosureInspected != true
            || snapshot.TypedReferences?.IsComplete != true)
            return HorseIdentityMigrationJournalReason.UnverifiedPages;
        return null;
    }

    private static async Task<IReadOnlyList<string>> ReadColumnsAsync(
        SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        if (table.Contains('"')) throw new InvalidDataException("Physical table name is invalid.");
        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) columns.Add(reader.GetString(1));
        if (columns.Count == 0 || columns.Any(column => column.Contains('"')))
            throw new InvalidDataException("Physical table columns are invalid.");
        return columns;
    }

    private static async Task UpdateChangedValuesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<string> columns,
        TypedHorseIdentityTransformedRow row,
        CancellationToken cancellationToken,
        TypedHorseIdentityExecutionBudget budget)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            budget.EnsureWithinBounds(cancellationToken);
            if (Equals(row.SourceValues[index], row.ExpectedValues[index])) continue;
            var column = columns[index];
            if (column.Contains('"')) throw new InvalidDataException("Physical column name is invalid.");
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"UPDATE \"{table}\" SET \"{column}\"=$value WHERE rowid=$rowid;";
            command.Parameters.AddWithValue("$value", row.ExpectedValues[index] ?? DBNull.Value);
            command.Parameters.AddWithValue("$rowid", row.RowId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("Physical page row changed during update.");
        }
    }

    private static bool FixedHashEquals(string left, string right)
    {
        if (left.Length != right.Length) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }
}
