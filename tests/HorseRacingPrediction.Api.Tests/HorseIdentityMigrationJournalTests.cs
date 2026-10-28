using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using System.Text.Json;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Api.Endpoints.IdentityMigration;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationJournalTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
    private const string ChildModeVariable = "HRP_HORSE_IDENTITY_JOURNAL_CHILD_MODE";
    private const string ChildDirectoryVariable = "HRP_HORSE_IDENTITY_JOURNAL_CHILD_DIRECTORY";
    private const string ChildOperationVariable = "HRP_HORSE_IDENTITY_JOURNAL_CHILD_OPERATION";
    private const string ChildReadyVariable = "HRP_HORSE_IDENTITY_JOURNAL_CHILD_READY";
    private static readonly ConditionalWeakTable<Process, BoundedProcessOutput> ChildOutput = new();

    [TestMethod]
    public async Task RecordFailureIsAtomicBoundedAndPreservesEarlierResults()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var staged = await fixture.Journal.StageAsync(operationId, Manifest(1), Now);
        Assert.IsTrue(staged.Succeeded);
        Assert.IsTrue((await fixture.Journal.SetResultsAsync(operationId,
            [new(HorseIdentityMigrationResultCode.PlanIncomplete, Count: 2)], Now.AddMinutes(1))).Succeeded);

        var failedAt = Now.AddMinutes(2);
        var recorded = await fixture.Journal.RecordFailureAsync(operationId,
            HorseIdentityMigrationResultCode.BackupFailed, failedAt);
        var readback = await fixture.Journal.GetStatusAsync(operationId);

        Assert.IsTrue(recorded.Succeeded);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Failed, readback!.Status);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.BackupFailed, readback.Reason);
        Assert.AreEqual(failedAt, readback.UpdatedAt);
        CollectionAssert.AreEqual(new[]
        {
            HorseIdentityMigrationResultCode.PlanIncomplete,
            HorseIdentityMigrationResultCode.BackupFailed,
        }, readback.Results.Select(result => result.Code).ToArray());
        Assert.AreEqual(operationId, (await fixture.Journal.GetMostRecentOperationAsync())!.OperationId);
    }

    [TestMethod]
    public async Task RecordFailurePreservesPartialStatusReasonAndEvidence()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var lease = await StartClaimedAsync(fixture, operationId);
        await fixture.Journal.SetResultsAsync(operationId,
            [new(HorseIdentityMigrationResultCode.OperationInterrupted, Count: 1)], Now);
        await lease.MarkPartialAsync(Now.AddMinutes(1));
        await lease.DisposeAsync();

        var recorded = await fixture.Journal.RecordFailureAsync(operationId,
            HorseIdentityMigrationResultCode.ApplyFailed, Now.AddMinutes(2));
        var readback = await fixture.Journal.GetStatusAsync(operationId);

        Assert.IsTrue(recorded.Succeeded);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.PartialUnverified, readback!.Status);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.OperationInterrupted, readback.Reason);
        CollectionAssert.AreEqual(new[]
        {
            HorseIdentityMigrationResultCode.OperationInterrupted,
            HorseIdentityMigrationResultCode.ApplyFailed,
        }, readback.Results.Select(result => result.Code).ToArray());
    }

    [TestMethod]
    public async Task RecordVerificationFailureDoesNotDowngradeSucceededDataEvidence()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var lease = await StartClaimedAsync(fixture, operationId);
        var evidence = Evidence(0);
        await fixture.Journal.RegisterRequiredPagesAsync(operationId, [Prepared(evidence)], Now);
        await AdvanceToVerifiedAsync(fixture.Journal, operationId, evidence);
        await lease.CompleteAsync(Now.AddMinutes(1));
        await lease.DisposeAsync();

        var originalTerminal = await ReadTerminalTimestampAsync(fixture.Journal.DatabasePath, operationId);
        var recorded = await fixture.Journal.RecordFailureAsync(operationId,
            HorseIdentityMigrationResultCode.VerificationFailed, Now.AddMinutes(2));
        var duplicate = await fixture.Journal.RecordFailureAsync(operationId,
            HorseIdentityMigrationResultCode.VerificationFailed, Now.AddMinutes(3));
        var readback = await fixture.Journal.GetStatusAsync(operationId);
        var retainedTerminal = await ReadTerminalTimestampAsync(fixture.Journal.DatabasePath, operationId);

        Assert.IsTrue(recorded.Succeeded);
        Assert.IsTrue(duplicate.Succeeded);
        Assert.AreEqual(originalTerminal, retainedTerminal, "Late failures must not rewrite terminal evidence.");
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded, readback!.Status);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.None, readback.Reason);
        Assert.AreEqual(HorseIdentityMigrationResultCode.VerificationFailed, readback.Results.Single().Code);
        Assert.AreEqual(Now.AddMinutes(3), readback.UpdatedAt);
    }

    [TestMethod]
    public void PreparationCancellationPolicyPreservesPriorOrdinalsAndAllowsOnlyKnownPreApplyingStates()
    {
        Assert.AreEqual(0, (int)HorseIdentityMigrationExecutionPhase.IntentRecorded);
        Assert.AreEqual(9, (int)HorseIdentityMigrationExecutionPhase.Verified);
        Assert.AreEqual(10, (int)HorseIdentityMigrationExecutionPhase.Failed);
        Assert.AreEqual(11, (int)HorseIdentityMigrationExecutionPhase.CancellationRequested);
        Assert.AreEqual(12, (int)HorseIdentityMigrationExecutionPhase.PreparationCancelled);

        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
        })
        {
            Assert.IsTrue(HorseIdentityMigrationExecutionPolicy.CanRequestPreparationCancellation(
                HorseIdentityMigrationOperationStatus.Staged, phase));
            Assert.IsFalse(HorseIdentityMigrationExecutionPolicy.CanRequestPreparationCancellation(
                HorseIdentityMigrationOperationStatus.PartialUnverified, phase));
        }

        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.IntentRecorded,
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.Applying,
            HorseIdentityMigrationExecutionPhase.DataSucceeded,
            HorseIdentityMigrationExecutionPhase.Verifying,
            HorseIdentityMigrationExecutionPhase.Verified,
            HorseIdentityMigrationExecutionPhase.Failed,
            HorseIdentityMigrationExecutionPhase.CancellationRequested,
            HorseIdentityMigrationExecutionPhase.PreparationCancelled,
        })
            Assert.IsFalse(HorseIdentityMigrationExecutionPolicy.CanRequestPreparationCancellation(
                HorseIdentityMigrationOperationStatus.Staged, phase), $"{phase} must not request cancellation.");

        Assert.IsTrue(HorseIdentityMigrationExecutionPolicy.CanTransition(
            HorseIdentityMigrationExecutionPhase.BackedUp,
            HorseIdentityMigrationExecutionPhase.CancellationRequested));
        Assert.IsTrue(HorseIdentityMigrationExecutionPolicy.CanTransition(
            HorseIdentityMigrationExecutionPhase.CancellationRequested,
            HorseIdentityMigrationExecutionPhase.PreparationCancelled));
        Assert.IsFalse(HorseIdentityMigrationExecutionPolicy.CanTransition(
            HorseIdentityMigrationExecutionPhase.CancellationRequested,
            HorseIdentityMigrationExecutionPhase.Applying));
        Assert.IsFalse(HorseIdentityMigrationExecutionPolicy.CanTransition(
            HorseIdentityMigrationExecutionPhase.PreparationCancelled,
            HorseIdentityMigrationExecutionPhase.Failed));
    }

    [TestMethod]
    public async Task PreparationCancellationRequestRacesApplyingInOneJournalWriteBoundary()
    {
        using var fixture = new Fixture();
        var (operationId, _) = await StageCancelableAsync(fixture, 81);

        var cancelTask = fixture.Journal.RequestPreparationCancellationAsync(operationId, Now.AddMinutes(1));
        var applyingTask = fixture.Journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Applying, Now.AddMinutes(1));
        await Task.WhenAll(cancelTask, applyingTask);
        var cancellation = await cancelTask;
        var applying = await applyingTask;

        Assert.AreEqual(1, Convert.ToInt32(cancellation.Succeeded) + Convert.ToInt32(applying.Succeeded),
            "Exactly one transaction may win the request-vs-Applying race.");
        var operation = await fixture.Journal.GetStatusAsync(operationId);
        var intent = await fixture.Journal.GetExecutionIntentAsync(operationId);
        Assert.IsNotNull(operation);
        Assert.IsNotNull(intent);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged, operation.Status);
        Assert.IsNull(intent.CancellationSourceSetHash);
        if (intent.Phase == HorseIdentityMigrationExecutionPhase.CancellationRequested)
        {
            Assert.AreEqual(HorseIdentityMigrationJournalReason.None, operation.Reason);
            Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
                (await fixture.Journal.AdvanceExecutionIntentAsync(operationId,
                    HorseIdentityMigrationExecutionPhase.Applying, Now.AddMinutes(2))).Reason);
        }
        else
        {
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.Applying, intent.Phase);
            Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
                (await fixture.Journal.RequestPreparationCancellationAsync(operationId, Now.AddMinutes(2))).Reason);
        }
    }

    [TestMethod]
    public async Task PreparationCancellationRequestIsIdempotentAndRejectsUnsafeOperationStates()
    {
        using var fixture = new Fixture();
        var (cancelId, _) = await StageCancelableAsync(fixture, 82);
        var first = await fixture.Journal.RequestPreparationCancellationAsync(cancelId, Now.AddMinutes(1));
        var duplicate = await fixture.Journal.RequestPreparationCancellationAsync(cancelId, Now.AddMinutes(2));
        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(duplicate.Succeeded);
        Assert.AreEqual(first.Operation!.OperationId, duplicate.Operation!.OperationId);
        Assert.AreEqual(HorseIdentityMigrationExecutionPhase.CancellationRequested, duplicate.Intent!.Phase);
        Assert.AreEqual(first.Intent!.UpdatedAt, duplicate.Intent.UpdatedAt,
            "An idempotent retry returns the stored request receipt without rewriting timestamps.");

        using var applyingFixture = new Fixture();
        var (applyingId, _) = await StageCancelableAsync(applyingFixture, 83);
        Assert.IsTrue((await applyingFixture.Journal.AdvanceExecutionIntentAsync(applyingId,
            HorseIdentityMigrationExecutionPhase.Applying, Now)).Succeeded);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await applyingFixture.Journal.RequestPreparationCancellationAsync(applyingId, Now.AddMinutes(1))).Reason);

        using var partialFixture = new Fixture();
        var (partialId, _) = await StageCancelableAsync(partialFixture, 84);
        await UpdateOperationAsync(partialFixture.Journal.DatabasePath, partialId,
            HorseIdentityMigrationOperationStatus.PartialUnverified,
            HorseIdentityMigrationJournalReason.OperationInterrupted, terminalAt: null);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await partialFixture.Journal.RequestPreparationCancellationAsync(partialId, Now.AddMinutes(1))).Reason);

        using var failedFixture = new Fixture();
        var (failedId, _) = await StageCancelableAsync(failedFixture, 85);
        await UpdateOperationAsync(failedFixture.Journal.DatabasePath, failedId,
            HorseIdentityMigrationOperationStatus.Failed,
            HorseIdentityMigrationJournalReason.BackupFailed, Now.ToString("O"));
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await failedFixture.Journal.RequestPreparationCancellationAsync(failedId, Now.AddMinutes(1))).Reason);

        using var mismatchedFixture = new Fixture();
        var (mismatchedId, _) = await StageCancelableAsync(mismatchedFixture, 86);
        await using (var connection = new SqliteConnection($"Data Source={mismatchedFixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE migration_request_keys SET immutable_request_hash = $hash WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$hash", new string('B', 64));
            command.Parameters.AddWithValue("$id", mismatchedId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await fixture.Journal.RequestPreparationCancellationAsync(mismatchedId, Now.AddMinutes(1))).Reason);
        Assert.AreEqual(HorseIdentityMigrationExecutionPhase.BackedUp,
            (await mismatchedFixture.Journal.GetExecutionIntentAsync(mismatchedId))!.Phase);
    }

    [TestMethod]
    public async Task PreparationCancellationDuplicateFinalizationRequiresConfiguredSourceSetMatch()
    {
        using var fixture = new Fixture();
        var (operationId, requestHash) = await StageCancelableAsync(fixture, 88);
        Assert.IsTrue((await fixture.Journal.RequestPreparationCancellationAsync(operationId, Now)).Succeeded);
        var sources = new[]
        {
            Path.Combine(fixture.Directory, "event.db"), Path.Combine(fixture.Directory, "collection.db"),
            Path.Combine(fixture.Directory, "schedule.db"), Path.Combine(fixture.Directory, "queue.db"),
        };
        var sourceHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sources);
        var proof = HorseIdentityMigrationMaintenanceCapability.CreatePreparationCancellationAuthorization(
            operationId, requestHash, sourceHash, 1, 1, new byte[32]);
        await MarkCancellationReceiptAsync(fixture.Journal.DatabasePath, operationId, sourceHash);

        var duplicate = await fixture.Journal.FinalizePreparationCancellationAsync(operationId, proof, sources,
            Now.AddMinutes(1));
        var wrongTarget = await fixture.Journal.FinalizePreparationCancellationAsync(operationId, proof,
            sources.Append(Path.Combine(fixture.Directory, "other.db")), Now.AddMinutes(2));

        Assert.IsTrue(duplicate.Succeeded);
        Assert.AreEqual(sourceHash, duplicate.Intent!.CancellationSourceSetHash);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.RequestConflict, wrongTarget.Reason,
            "A terminal receipt must not be accepted for a different configured source set after gates disappear.");
    }

    [TestMethod]
    public async Task PreparationCancellationReceiptIsNotPrunedAndLegacyIndexUpgradesBeforeWrites()
    {
        using var fixture = new Fixture();
        var (cancelledId, requestHash) = await StageCancelableAsync(fixture, 89);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX IX_migration_execution_intents_single_active; CREATE UNIQUE INDEX IX_migration_execution_intents_single_active ON migration_execution_intents ((1)) WHERE phase NOT IN (9, 10);";
            await command.ExecuteNonQueryAsync();
        }

        Assert.IsTrue((await fixture.Journal.RequestPreparationCancellationAsync(cancelledId, Now)).Succeeded);
        var upgradedIndex = await ReadActiveIntentIndexAsync(fixture.Journal.DatabasePath);
        StringAssert.Contains(upgradedIndex!, "phase NOT IN (9, 10, 12)");

        var receiptHash = new string('A', 64);
        await MarkCancellationReceiptAsync(fixture.Journal.DatabasePath, cancelledId, receiptHash,
            Now.AddDays(-365).ToString("O"));
        var (nextId, nextHash) = await StageCancelableAsync(fixture, 90);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(nextId, nextHash, Now)).Succeeded,
            "A legacy-index terminal cancellation must not block a later intent after write-path upgrade.");
        Assert.IsNotNull(await fixture.Journal.GetStatusAsync(cancelledId),
            "Cancellation receipt is durable control evidence and remains protected from retention pruning.");
    }

    [TestMethod]
    public async Task PreparationCancellationFinalizerRejectsUnprovenCapabilityWithoutMutation()
    {
        using var fixture = new Fixture();
        var (operationId, requestHash) = await StageCancelableAsync(fixture, 87);
        Assert.IsTrue((await fixture.Journal.RequestPreparationCancellationAsync(operationId, Now.AddMinutes(1)))
            .Succeeded);
        var beforeOperation = await fixture.Journal.GetStatusAsync(operationId);
        var beforeIntent = await fixture.Journal.GetExecutionIntentAsync(operationId);
        var proof = HorseIdentityMigrationMaintenanceCapability.CreatePreparationCancellationAuthorization(
            operationId, requestHash, new string('F', 64), 1, 1, new byte[32]);
        var sources = new[]
        {
            Path.Combine(fixture.Directory, "event.db"), Path.Combine(fixture.Directory, "collection.db"),
            Path.Combine(fixture.Directory, "schedule.db"), Path.Combine(fixture.Directory, "queue.db"),
        };

        var rejected = await fixture.Journal.FinalizePreparationCancellationAsync(operationId, proof, sources,
            Now.AddMinutes(2));

        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation, rejected.Reason);
        Assert.AreEqual(beforeOperation, await fixture.Journal.GetStatusAsync(operationId));
        Assert.AreEqual(beforeIntent, await fixture.Journal.GetExecutionIntentAsync(operationId));
        Assert.IsNull((await fixture.Journal.GetExecutionIntentAsync(operationId))!.CancellationSourceSetHash);
    }

    [TestMethod]
    public async Task PreparationCancellationProofFailureIsSafeDurableAndUpgradesLegacyIntentInPlace()
    {
        using var fixture = new Fixture();
        var (operationId, requestHash) = await StageCancelableAsync(fixture, 91);
        Assert.IsTrue((await fixture.Journal.RequestPreparationCancellationAsync(operationId, Now)).Succeeded);

        // Simulate an existing journal schema before this nullable diagnostic column was introduced.
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE migration_execution_intents DROP COLUMN cancellation_diagnostic_reason;";
            await command.ExecuteNonQueryAsync();
        }

        var failedAt = Now.AddMinutes(2);
        var recorded = await fixture.Journal.RecordPreparationCancellationDiagnosticAsync(operationId, failedAt);
        var duplicate = await fixture.Journal.RecordPreparationCancellationDiagnosticAsync(operationId,
            Now.AddMinutes(3));
        Assert.IsTrue(recorded.Succeeded);
        Assert.IsTrue(duplicate.Succeeded);
        Assert.AreEqual(HorseIdentityMigrationExecutionPhase.CancellationRequested, recorded.Intent!.Phase);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.PreparationFailed,
            recorded.Intent.CancellationDiagnosticReason);
        Assert.AreEqual(failedAt, recorded.Intent.UpdatedAt);
        Assert.AreEqual(failedAt, duplicate.Intent!.UpdatedAt, "Repeated refusal must not churn diagnostic time.");
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged, recorded.Operation!.Status);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.None, recorded.Operation.Reason,
            "A refusal keeps the cancellation intent active rather than converting it to a generic failure.");
        Assert.AreEqual("PreparationFailed", HorseIdentityMigrationEndpoints.ToPublic(recorded.Operation,
            saved: null, pending: null, recorded.Intent).Reason);
        Assert.AreEqual(failedAt, HorseIdentityMigrationEndpoints.ToPublic(recorded.Operation,
            saved: null, pending: null, recorded.Intent).ExecutionUpdatedAt);
        Assert.AreEqual(requestHash, recorded.Intent.ImmutableRequestHash,
            "The old row must retain its canonical request binding during the nullable-column upgrade.");
    }

    private static async Task<(Guid OperationId, string RequestHash)> StageCancelableAsync(Fixture fixture, int index)
    {
        var operationId = Guid.NewGuid();
        var manifest = Manifest(index);
        var fingerprint = new string('A', 64);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        Assert.IsTrue((await fixture.Journal.StageWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
            requestHash, fingerprint, Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(operationId, requestHash, Now)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
        })
            Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(operationId, phase, Now)).Succeeded,
                $"Expected to advance test intent to {phase}.");

        return (operationId, requestHash);
    }

    private static async Task UpdateOperationAsync(string databasePath, Guid operationId,
        HorseIdentityMigrationOperationStatus status, HorseIdentityMigrationJournalReason reason, string? terminalAt)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE migration_operations SET status = $status, reason = $reason, terminal_utc = $terminal WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$reason", (int)reason);
        command.Parameters.AddWithValue("$terminal", (object?)terminalAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task MarkCancellationReceiptAsync(string databasePath, Guid operationId, string sourceHash,
        string? terminalAt = null)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using (var intent = connection.CreateCommand())
        {
            intent.CommandText = "UPDATE migration_execution_intents SET phase = $phase, cancellation_source_set_hash = $source WHERE operation_id = $id;";
            intent.Parameters.AddWithValue("$phase", (int)HorseIdentityMigrationExecutionPhase.PreparationCancelled);
            intent.Parameters.AddWithValue("$source", sourceHash);
            intent.Parameters.AddWithValue("$id", operationId.ToString("D"));
            Assert.AreEqual(1, await intent.ExecuteNonQueryAsync());
        }
        await UpdateOperationAsync(databasePath, operationId, HorseIdentityMigrationOperationStatus.Failed,
            HorseIdentityMigrationJournalReason.PreparationCancelled, terminalAt ?? Now.ToString("O"));
    }

    private static async Task<string?> ReadActiveIntentIndexAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_migration_execution_intents_single_active';";
        return await command.ExecuteScalarAsync() as string;
    }

    [TestMethod]
    public void PublicMapperKeepsNewSafeFailureAtResultCapWithoutMutatingStoredResults()
    {
        var operation = new HorseIdentityMigrationJournalOperation(Guid.NewGuid(), new string('a', 64),
            HorseIdentityMigrationOperationStatus.Failed, HorseIdentityMigrationJournalReason.BackupFailed,
            Now, Now.AddMinutes(1), Enumerable.Repeat(
                new HorseIdentityMigrationJournalResultItem(HorseIdentityMigrationResultCode.PlanIncomplete), 128)
                .Append(new(HorseIdentityMigrationResultCode.BackupFailed)).ToArray());

        var response = HorseIdentityMigrationPublicMapper.ToPublic(operation);

        Assert.AreEqual(129, operation.Results.Count, "Mapping must not discard persisted evidence.");
        Assert.AreEqual(128, response.Results.Count);
        Assert.AreEqual("BackupFailed", response.Results[^1].Code);
        Assert.AreEqual(127, response.Results.Count(result => result.Code == "PlanIncomplete"));
    }

    [TestMethod]
    public async Task MostRecentJournalReadHonorsCancellation()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(1), Now)).Succeeded);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await fixture.Journal.GetMostRecentOperationAsync(cancellation.Token);
            Assert.Fail("A canceled status read must not continue as an uncanceled journal read.");
        }
        catch (OperationCanceledException)
        {
            // Expected: status inspection must honor caller cancellation.
        }
    }

    private static async Task<string?> ReadTerminalTimestampAsync(string databasePath, Guid operationId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT terminal_utc FROM migration_operations WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return await command.ExecuteScalarAsync() as string;
    }

    [TestMethod]
    public async Task SameGuidAndValidatedManifestIsIdempotentButChangedManifestConflicts()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var first = await fixture.Journal.StageAsync(operationId, Manifest(1), Now);
        var duplicate = await fixture.Journal.StageAsync(operationId, Manifest(1), Now.AddMinutes(1));
        var changed = await fixture.Journal.StageAsync(operationId, Manifest(2), Now.AddMinutes(2));

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(duplicate.Succeeded);
        Assert.AreEqual(first.Operation!.RequestHash, duplicate.Operation!.RequestHash);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged, duplicate.Operation.Status);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.RequestConflict, changed.Reason);
        Assert.AreEqual(first.Operation.RequestHash, changed.Operation!.RequestHash);
    }

    [TestMethod]
    public async Task RequestKeyReplaysSameOperationAndRejectsChangedContents()
    {
        using var fixture = new Fixture();
        var requestKey = Guid.NewGuid();
        var manifest = Manifest(1);
        var fingerprint = new string('A', 64);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);

        var first = await fixture.Journal.StageWithRequestKeyAsync(
            Guid.NewGuid(), manifest, requestKey, requestHash, fingerprint, Now);
        var replay = await fixture.Journal.StageWithRequestKeyAsync(
            Guid.NewGuid(), manifest, requestKey, requestHash, fingerprint, Now.AddMinutes(1));
        var changedManifest = await fixture.Journal.StageWithRequestKeyAsync(
            Guid.NewGuid(), Manifest(2), requestKey,
            HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(Manifest(2), fingerprint), fingerprint,
            Now.AddMinutes(2));

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(replay.Succeeded);
        Assert.AreEqual(first.Operation!.OperationId, replay.Operation!.OperationId);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.RequestConflict, changedManifest.Reason);
        Assert.AreEqual(first.Operation.OperationId, changedManifest.Operation!.OperationId);
        var lookup = await fixture.Journal.GetByRequestKeyAsync(requestKey);
        Assert.IsNotNull(lookup);
        Assert.AreEqual(first.Operation.OperationId, lookup.Operation.OperationId);
        Assert.AreEqual(fingerprint, lookup.PreviewFingerprint);
    }

    [TestMethod]
    public async Task PendingRequestSurvivesFreshJournalReadbackAndDoesNotBlockStartupOrAcquireFingerprint()
    {
        using var fixture = new Fixture();
        var manifest = Manifest(21);
        var requestKey = Guid.NewGuid();
        var pendingHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        var staged = await fixture.Journal.StagePendingWithRequestKeyAsync(Guid.NewGuid(), manifest, requestKey,
            pendingHash, Now);
        Assert.IsTrue(staged.Succeeded);

        var freshJournal = new HorseIdentityMigrationJournal(fixture.Directory, []);
        var guard = await freshJournal.InspectStartupGuardAsync(Now.AddMinutes(1));
        var key = await freshJournal.GetByRequestKeyAsync(requestKey);
        var pending = await freshJournal.GetPendingRequestAsync(staged.Operation!.OperationId);

        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready, guard.State,
            "A staged pending request is not an interrupted execution and does not block normal API startup.");
        Assert.IsNotNull(key);
        Assert.AreEqual(HorseIdentityMigrationAdmissionKind.PendingV2, key.RequestKind);
        Assert.IsNull(key.PreviewFingerprint);
        Assert.IsNotNull(pending);
        Assert.AreEqual(pendingHash, pending.ImmutableRequestHash);
        Assert.AreEqual(JsonSerializer.Serialize(manifest.Entries), JsonSerializer.Serialize(pending.Manifest.Entries));
        Assert.IsNull(await freshJournal.GetSavedRequestAsync(staged.Operation.OperationId),
            "The strict bound-v1 reader must not reinterpret a pending request as fingerprint-bound.");
        Assert.IsNull(await freshJournal.GetExecutionIntentAsync(staged.Operation.OperationId));
    }

    [TestMethod]
    public async Task PendingSealIsAtomicIdempotentAndRequiredOnlyAtApplying()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var manifest = Manifest(23);
        var requestKey = Guid.NewGuid();
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.IsTrue((await fixture.Journal.StagePendingWithRequestKeyAsync(operationId, manifest, requestKey,
            requestHash, Now)).Succeeded);
        var executable = await fixture.Journal.GetExecutableRequestAsync(operationId);
        Assert.IsNotNull(executable);
        Assert.AreEqual(HorseIdentityMigrationAdmissionKind.PendingV2, executable.RequestKind);
        Assert.AreEqual(requestHash, executable.ImmutableRequestHash);
        Assert.IsNull(executable.PreviewFingerprint);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(operationId, requestHash, Now)).Succeeded);

        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
        })
            Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(operationId, phase, Now)).Succeeded);

        var fingerprint = new string('1', 64);
        var sourceHash = new string('2', 64);
        var backupHash = new string('3', 64);
        var concurrentSeals = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            fixture.Journal.SealPendingRequestAsync(operationId, requestHash, fingerprint,
                sourceHash, backupHash, Now)));
        Assert.IsTrue(concurrentSeals.All(outcome => outcome.Succeeded),
            string.Join(';', concurrentSeals.Select(outcome => outcome.Reason)));
        Assert.AreEqual(1, concurrentSeals.Select(outcome => outcome.Seal).Distinct().Count(),
            "Concurrent identical seal attempts must converge on one immutable durable record.");
        var seal = concurrentSeals[0];
        var sameSeal = await fixture.Journal.SealPendingRequestAsync(operationId, requestHash, fingerprint,
            sourceHash, backupHash, Now.AddMinutes(1));
        Assert.IsTrue(sameSeal.Succeeded, "An identical retry must return the durable seal.");
        Assert.AreEqual(seal.Seal, sameSeal.Seal);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.RequestConflict,
            (await fixture.Journal.SealPendingRequestAsync(operationId, requestHash, fingerprint,
                sourceHash, new string('4', 64), Now)).Reason);
        Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Applying, Now)).Succeeded);

        var pendingAfterSeal = await fixture.Journal.GetPendingRequestAsync(operationId);
        var executableAfterSeal = await fixture.Journal.GetExecutableRequestAsync(operationId);
        Assert.IsNotNull(pendingAfterSeal);
        Assert.IsNotNull(executableAfterSeal);
        Assert.AreEqual(requestHash, pendingAfterSeal.ImmutableRequestHash);
        Assert.AreEqual(requestHash, executableAfterSeal.ImmutableRequestHash,
            "Sealing adds full evidence without changing the immutable pending request identity.");
        Assert.AreEqual(HorseIdentityMigrationAdmissionKind.PendingV2, executableAfterSeal.RequestKind);
        Assert.IsNull(await fixture.Journal.GetSavedRequestAsync(operationId),
            "The strict BoundV1 reader must not reinterpret a sealed pending request.");
    }

    [TestMethod]
    public async Task PendingCannotSealBeforeIntentAndBackedUpOrAdvanceToApplyingWithoutSeal()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var manifest = Manifest(24);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.IsTrue((await fixture.Journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
            requestHash, Now)).Succeeded);
        var fingerprint = new string('5', 64);
        var sourceHash = new string('6', 64);
        var backupHash = new string('7', 64);

        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await fixture.Journal.SealPendingRequestAsync(operationId, requestHash, fingerprint, sourceHash,
                backupHash, Now)).Reason);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(operationId, requestHash, Now)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
        })
            Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(operationId, phase, Now)).Succeeded);

        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await fixture.Journal.AdvanceExecutionIntentAsync(operationId,
                HorseIdentityMigrationExecutionPhase.Applying, Now)).Reason);
        Assert.AreEqual(HorseIdentityMigrationExecutionPhase.BackedUp,
            (await fixture.Journal.GetExecutionIntentAsync(operationId))!.Phase);
        Assert.IsNull(await fixture.Journal.GetPendingSealAsync(operationId));
    }

    [TestMethod]
    public async Task SameRequestKeyCannotCoalesceBoundV1AndPendingV2OrLaunderPendingThroughStandaloneBinding()
    {
        using var fixture = new Fixture();
        var manifest = Manifest(22);
        var requestKey = Guid.NewGuid();
        var fingerprint = new string('7', 64);
        var boundHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        var bound = await fixture.Journal.StageWithRequestKeyAsync(Guid.NewGuid(), manifest, requestKey,
            boundHash, fingerprint, Now);
        Assert.IsTrue(bound.Succeeded);

        var pendingHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.AreNotEqual(boundHash, pendingHash);
        var asPending = await fixture.Journal.StagePendingWithRequestKeyAsync(Guid.NewGuid(), manifest, requestKey,
            pendingHash, Now.AddMinutes(1));
        Assert.AreEqual(HorseIdentityMigrationJournalReason.RequestConflict, asPending.Reason);
        Assert.AreEqual(bound.Operation!.OperationId, asPending.Operation!.OperationId);

        var original = await fixture.Journal.GetByRequestKeyAsync(requestKey);
        Assert.IsNotNull(original);
        Assert.AreEqual(HorseIdentityMigrationAdmissionKind.BoundV1, original.RequestKind);
        Assert.AreEqual(boundHash, original.ImmutableRequestHash);
        Assert.AreEqual(fingerprint, original.PreviewFingerprint);

        var pendingKey = Guid.NewGuid();
        var pending = await fixture.Journal.StagePendingWithRequestKeyAsync(Guid.NewGuid(), manifest, pendingKey,
            pendingHash, Now.AddMinutes(2));
        Assert.IsTrue(pending.Succeeded);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.RequestConflict,
            (await fixture.Journal.BindStandaloneRequestAsync(pending.Operation!.OperationId, manifest, fingerprint,
                Now.AddMinutes(3))).Reason,
            "The old standalone bind path must not convert PendingV2 into BoundV1.");
        Assert.AreEqual(HorseIdentityMigrationAdmissionKind.PendingV2,
            (await fixture.Journal.GetByRequestKeyAsync(pendingKey))!.RequestKind);
        Assert.IsNull(await fixture.Journal.GetSavedRequestAsync(pending.Operation.OperationId));
    }

    [TestMethod]
    public async Task StandaloneLegacyBindingStoresFreshFingerprintAndIsIdempotent()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var manifest = Manifest(1);
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, manifest, Now)).Succeeded);
        var fingerprint = new string('C', 64);

        Assert.IsTrue((await fixture.Journal.BindStandaloneRequestAsync(operationId, manifest, fingerprint, Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.BindStandaloneRequestAsync(operationId, manifest, fingerprint, Now.AddMinutes(1))).Succeeded);
        var saved = await fixture.Journal.GetSavedRequestAsync(operationId);
        Assert.IsNotNull(saved);
        Assert.AreEqual(fingerprint, saved.PreviewFingerprint);
        Assert.AreEqual(HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint),
            saved.ImmutableRequestHash);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.RequestConflict,
            (await fixture.Journal.BindStandaloneRequestAsync(operationId, manifest, new string('D', 64), Now))
                .Reason);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(operationId, saved.ImmutableRequestHash, Now))
            .Succeeded);
    }

    [TestMethod]
    public async Task ConcurrentRequestKeyAdmissionCreatesExactlyOneOperation()
    {
        using var fixture = new Fixture();
        var requestKey = Guid.NewGuid();
        var manifest = Manifest(1);
        var fingerprint = new string('B', 64);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            fixture.Journal.StageWithRequestKeyAsync(Guid.NewGuid(), manifest, requestKey, requestHash,
                fingerprint, Now.AddTicks(index))));

        Assert.IsTrue(outcomes.All(outcome => outcome.Succeeded),
            string.Join(';', outcomes.Select(outcome => outcome.Reason)));
        Assert.AreEqual(1, outcomes.Select(outcome => outcome.Operation!.OperationId).Distinct().Count());
        var journalFiles = System.IO.Directory.GetFiles(fixture.Directory, "horse-identity-migration-journal.db*");
        Assert.HasCount(1, journalFiles);
        await using var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        Assert.AreEqual(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM migration_operations;"));
        Assert.AreEqual(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM migration_request_keys;"));
    }

    [TestMethod]
    public async Task RequestKeyCapacityReservationPreservesJournalBound()
    {
        using var fixture = new Fixture(maximumBytes: 2 * 1024 * 1024, reserveBytes: 64 * 1024);
        var manifest = Manifest(1);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, new string('C', 64));
        var requestKey = Guid.NewGuid();
        var outcome = await fixture.Journal.StageWithRequestKeyAsync(
            Guid.NewGuid(), manifest, requestKey, requestHash, new string('C', 64), Now);

        Assert.IsTrue(outcome.Succeeded);
        Assert.IsTrue(new FileInfo(fixture.Journal.DatabasePath).Length <= 2 * 1024 * 1024);
    }

    [TestMethod]
    public async Task LegacyV1JournalReadsAndUpgradesKeyedAdmissionWithoutPreviewColumn()
    {
        using var fixture = new Fixture();
        var legacyOperationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(legacyOperationId, Manifest(1), Now)).Succeeded);
        var legacyKey = Guid.NewGuid();
        var legacyHash = new string('A', 64);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE migration_request_keys; DROP TABLE migration_execution_intents; CREATE TABLE migration_request_keys (request_key TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL, operation_id TEXT NOT NULL UNIQUE, FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE); INSERT INTO migration_request_keys (request_key, immutable_request_hash, operation_id) VALUES ($key, $hash, $id);";
            command.Parameters.AddWithValue("$key", legacyKey.ToString("D"));
            command.Parameters.AddWithValue("$hash", legacyHash);
            command.Parameters.AddWithValue("$id", legacyOperationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var startupGuard = await fixture.Journal.InspectStartupGuardAsync(Now);
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready, startupGuard.State,
            "A legacy journal must be upgraded before strict startup validation.");

        await using (var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await inspect.OpenAsync();
            await using var command = inspect.CreateCommand();
            command.CommandText = "SELECT immutable_request_hash || '|' || operation_id FROM migration_request_keys WHERE request_key = $key;";
            command.Parameters.AddWithValue("$key", legacyKey.ToString("D"));
            Assert.AreEqual($"{legacyHash}|{legacyOperationId:D}", Convert.ToString(await command.ExecuteScalarAsync()));
        }

        var legacy = await fixture.Journal.GetByRequestKeyAsync(legacyKey);
        Assert.IsNotNull(legacy);
        Assert.IsNull(legacy.PreviewFingerprint);
        Assert.AreEqual(legacyOperationId, legacy.Operation.OperationId);

        var manifest = Manifest(2);
        var fingerprint = new string('B', 64);
        var hash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        var admittedKey = Guid.NewGuid();
        var admitted = await fixture.Journal.StageWithRequestKeyAsync(Guid.NewGuid(), manifest, admittedKey, hash,
            fingerprint, Now);
        Assert.IsTrue(admitted.Succeeded);
        var keyed = await fixture.Journal.GetByRequestKeyAsync(admittedKey);
        Assert.IsNotNull(keyed);
        Assert.AreEqual(fingerprint, keyed.PreviewFingerprint);
    }

    [TestMethod]
    public async Task LegacyStartupUpgradeIsIdempotentAndPreservesRowsUnderConcurrentReaders()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(3), Now)).Succeeded);
        var original = await fixture.Journal.GetStatusAsync(operationId);
        Assert.IsNotNull(original);
        string?[] originalOperationRow;
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            originalOperationRow = await ReadOperationRowAsync(connection, operationId);
        }
        var requestKey = Guid.NewGuid();
        var requestHash = new string('C', 64);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE migration_request_keys; DROP TABLE migration_execution_intents; DROP TABLE migration_pending_seals; CREATE TABLE migration_request_keys (request_key TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL, operation_id TEXT NOT NULL UNIQUE, FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE); INSERT INTO migration_request_keys (request_key, immutable_request_hash, operation_id) VALUES ($key, $hash, $id);";
            command.Parameters.AddWithValue("$key", requestKey.ToString("D"));
            command.Parameters.AddWithValue("$hash", requestHash);
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var concurrentJournals = Enumerable.Range(0, 8)
            .Select(_ => new HorseIdentityMigrationJournal(fixture.Directory, []))
            .ToArray();
        var concurrent = await Task.WhenAll(concurrentJournals
            .Select(journal => journal.InspectStartupGuardAsync(Now)));
        Assert.IsTrue(concurrent.All(guard => guard.State is HorseIdentityMigrationStartupState.Ready
            or HorseIdentityMigrationStartupState.OwnerActive));
        for (var index = 0; index < 3; index++)
            Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
                (await fixture.Journal.InspectStartupGuardAsync(Now)).State);
        var restored = await fixture.Journal.GetStatusAsync(operationId);
        Assert.IsNotNull(restored);
        Assert.AreEqual(original.RequestHash, restored.RequestHash);
        Assert.AreEqual(original.Status, restored.Status);

        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        CollectionAssert.AreEqual(originalOperationRow, await ReadOperationRowAsync(inspect, operationId));
        Assert.AreEqual(1L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM migration_request_keys WHERE request_key = '" + requestKey.ToString("D")
            + "' AND immutable_request_hash = '" + requestHash + "' AND operation_id = '" + operationId.ToString("D")
            + "' AND request_kind = 'BoundV1';"));
        Assert.AreEqual(2L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM pragma_table_info('migration_request_keys') WHERE name IN ('preview_fingerprint', 'request_kind');"));
        Assert.AreEqual(1L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'migration_pending_seals';"));
    }

    [TestMethod]
    public async Task PreAdmissionEmptyJournalCreatesOnlyCanonicalSupplementalSchema()
    {
        using var fixture = new Fixture();
        await HistoricalHorseIdentityMigrationJournalFixture.CreateAsync(fixture.Journal.DatabasePath,
            fixture.Journal.InitializationMarkerPath, fixture.Journal.LockPath);

        var upgraded = await fixture.Journal.InspectStartupGuardAsync(Now.AddMinutes(1));
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready, upgraded.State);

        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_operations;"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_request_keys;"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_execution_intents;"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_pending_seals;"));
        Assert.AreEqual(1L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM pragma_index_list('migration_execution_intents') WHERE name = 'IX_migration_execution_intents_single_active' AND \"unique\" = 1 AND partial = 1;"));
    }

    [TestMethod]
    public async Task PreAdmissionNonemptyStagedJournalPreservesRowsWithoutSynthesizingAdmissionRecords()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        await HistoricalHorseIdentityMigrationJournalFixture.CreateAsync(fixture.Journal.DatabasePath,
            fixture.Journal.InitializationMarkerPath, fixture.Journal.LockPath, operationId, Manifest(31), Now);

        string?[] before;
        await using (var snapshot = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await snapshot.OpenAsync();
            before = await ReadOperationRowAsync(snapshot, operationId);
        }
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(Now.AddMinutes(1))).State);
        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        CollectionAssert.AreEqual(before, await ReadOperationRowAsync(inspect, operationId));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_request_keys;"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_execution_intents;"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_pending_seals;"));
    }

    [TestMethod]
    public async Task PreAdmissionSchemaUpgradeIsIdempotentUnderConcurrentStartup()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        await HistoricalHorseIdentityMigrationJournalFixture.CreateAsync(fixture.Journal.DatabasePath,
            fixture.Journal.InitializationMarkerPath, fixture.Journal.LockPath, operationId, Manifest(35), Now);
        string?[] original;
        await using (var before = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await before.OpenAsync();
            original = await ReadOperationRowAsync(before, operationId);
        }

        var journals = Enumerable.Range(0, 8)
            .Select(_ => new HorseIdentityMigrationJournal(fixture.Directory, []))
            .ToArray();
        var outcomes = await Task.WhenAll(journals.Select(journal => journal.InspectStartupGuardAsync(Now)));
        Assert.IsTrue(outcomes.All(outcome => outcome.State is HorseIdentityMigrationStartupState.Ready
            or HorseIdentityMigrationStartupState.OwnerActive), string.Join(',', outcomes.Select(outcome => outcome.State)));
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(Now.AddMinutes(1))).State);

        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        CollectionAssert.AreEqual(original, await ReadOperationRowAsync(inspect, operationId));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_request_keys;"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_execution_intents;"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_pending_seals;"));
    }

    [TestMethod]
    public async Task MalformedHistoricalKeyOrIntentTableFailsBeforeAnySupplementalUpgrade()
    {
        foreach (var malformedTable in new[] { "migration_request_keys", "migration_execution_intents" })
        {
            using var fixture = new Fixture();
            await HistoricalHorseIdentityMigrationJournalFixture.CreateAsync(fixture.Journal.DatabasePath,
                fixture.Journal.InitializationMarkerPath, fixture.Journal.LockPath);
            await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = malformedTable == "migration_request_keys"
                    ? "CREATE TABLE migration_request_keys (request_key TEXT PRIMARY KEY NOT NULL);"
                    : "CREATE TABLE migration_execution_intents (operation_id TEXT PRIMARY KEY NOT NULL, phase INTEGER NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
                (await fixture.Journal.InspectStartupGuardAsync(Now)).State, malformedTable);
            await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
            await inspect.OpenAsync();
            Assert.AreEqual(1L, await ScalarInt64Async(inspect,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('migration_request_keys', 'migration_execution_intents', 'migration_pending_seals');"),
                $"Only the malformed pre-existing {malformedTable} table should remain; all newly added structures must roll back.");
            Assert.AreEqual(0L, await ScalarInt64Async(inspect,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_migration_execution_intents_single_active';"),
                malformedTable);
        }
    }

    [TestMethod]
    public async Task MalformedPresentSealRollsBackNewSupplementalTables()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(Now)).State);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX IX_migration_execution_intents_single_active; DROP TABLE migration_pending_seals; DROP TABLE migration_execution_intents; DROP TABLE migration_request_keys; CREATE TABLE migration_pending_seals (operation_id TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
            (await fixture.Journal.InspectStartupGuardAsync(Now.AddMinutes(1))).State);
        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('migration_request_keys', 'migration_execution_intents');"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_migration_execution_intents_single_active';"));
    }

    [TestMethod]
    public async Task DuplicateActiveIntentsFailClosedAndRollbackMissingAdmissionTables()
    {
        using var fixture = new Fixture();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(firstId, Manifest(32), Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.StageAsync(secondId, Manifest(33), Now)).Succeeded);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX IX_migration_execution_intents_single_active; DROP TABLE migration_pending_seals; DROP TABLE migration_execution_intents; DROP TABLE migration_request_keys; CREATE TABLE migration_execution_intents (operation_id TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, phase INTEGER NOT NULL, FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE); INSERT INTO migration_execution_intents VALUES ($first, $hash, '2026-10-15T00:00:00.0000000+00:00', '2026-10-15T00:00:00.0000000+00:00', 0); INSERT INTO migration_execution_intents VALUES ($second, $hash, '2026-10-15T00:00:00.0000000+00:00', '2026-10-15T00:00:00.0000000+00:00', 0);";
            command.Parameters.AddWithValue("$first", firstId.ToString("D"));
            command.Parameters.AddWithValue("$second", secondId.ToString("D"));
            command.Parameters.AddWithValue("$hash", new string('A', 64));
            await command.ExecuteNonQueryAsync();
        }

        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
            (await fixture.Journal.InspectStartupGuardAsync(Now.AddMinutes(1))).State);
        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('migration_request_keys', 'migration_pending_seals');"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_migration_execution_intents_single_active';"));
        Assert.AreEqual(2L, await ScalarInt64Async(inspect, "SELECT COUNT(*) FROM migration_execution_intents;"));
    }

    [TestMethod]
    public async Task InvalidLegacyOperationStateRollsBackSupplementalSchemaUpgrade()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(34), Now)).Succeeded);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX IX_migration_execution_intents_single_active; DROP TABLE migration_pending_seals; DROP TABLE migration_execution_intents; DROP TABLE migration_request_keys; UPDATE migration_operations SET status = 99 WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
            (await fixture.Journal.InspectStartupGuardAsync(Now.AddMinutes(1))).State);
        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        Assert.AreEqual(99L, await ScalarInt64Async(inspect,
            "SELECT status FROM migration_operations WHERE operation_id = '" + operationId.ToString("D") + "';"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('migration_request_keys', 'migration_execution_intents', 'migration_pending_seals');"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_migration_execution_intents_single_active';"));
    }

    [TestMethod]
    public async Task MalformedCanonicalIntentIndexFailsClosedAndRollsBackMissingTables()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(Now)).State);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX IX_migration_execution_intents_single_active; DROP TABLE migration_pending_seals; DROP TABLE migration_request_keys; CREATE UNIQUE INDEX IX_migration_execution_intents_single_active ON migration_execution_intents(phase) WHERE phase NOT IN (9, 10);";
            await command.ExecuteNonQueryAsync();
        }

        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
            (await fixture.Journal.InspectStartupGuardAsync(Now.AddMinutes(1))).State);
        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('migration_request_keys', 'migration_pending_seals');"));
        Assert.AreEqual(1L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_migration_execution_intents_single_active';"));
    }

    [TestMethod]
    public async Task InvalidLegacyRequestKindRollsBackOperationalSchemaUpgrade()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(4), Now)).Succeeded);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE migration_request_keys; DROP TABLE migration_execution_intents; DROP TABLE migration_pending_seals; CREATE TABLE migration_request_keys (request_key TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL, request_kind TEXT NOT NULL, operation_id TEXT NOT NULL UNIQUE, FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE); INSERT INTO migration_request_keys (request_key, immutable_request_hash, request_kind, operation_id) VALUES ($key, $hash, 'UnknownKind', $id);";
            command.Parameters.AddWithValue("$key", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$hash", new string('D', 64));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var guard = await fixture.Journal.InspectStartupGuardAsync(Now);
        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt, guard.State);

        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM pragma_table_info('migration_request_keys') WHERE name = 'preview_fingerprint';"));
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'migration_pending_seals';"));
    }

    [TestMethod]
    public async Task MissingRequiredCoreTableFailsClosedWithoutReconstruction()
    {
        using var fixture = new Fixture();
        Assert.IsTrue((await fixture.Journal.StageAsync(Guid.NewGuid(), Manifest(5), Now)).Succeeded);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE migration_request_keys; DROP TABLE migration_execution_intents; DROP TABLE migration_pending_seals; DROP TABLE migration_operations;";
            await command.ExecuteNonQueryAsync();
        }

        var guard = await fixture.Journal.InspectStartupGuardAsync(Now);
        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt, guard.State);

        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        Assert.AreEqual(0L, await ScalarInt64Async(inspect,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'migration_operations';"));
    }

    [TestMethod]
    public async Task ExecutionIntentRequiresExplicitKeyAndRejectsBackwardsOrSkippedPhases()
    {
        Assert.IsFalse(HorseIdentityMigrationExecutionPolicy.CanTransition(
            HorseIdentityMigrationExecutionPhase.IntentRecorded,
            HorseIdentityMigrationExecutionPhase.BackedUp));
        Assert.IsFalse(HorseIdentityMigrationExecutionPolicy.CanTransition(
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.IntentRecorded));
        Assert.IsFalse(HorseIdentityMigrationExecutionPolicy.CanTransition(
            HorseIdentityMigrationExecutionPhase.Verified,
            HorseIdentityMigrationExecutionPhase.Failed));

        using var fixture = new Fixture();
        var legacyId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(legacyId, Manifest(1), Now)).Succeeded);
        var legacyIntent = await fixture.Journal.CreateExecutionIntentAsync(legacyId,
            new string('D', 64), Now);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation, legacyIntent.Reason);

        var keyedId = Guid.NewGuid();
        var fingerprint = new string('E', 64);
        var manifest = Manifest(2);
        var immutableHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        Assert.IsTrue((await fixture.Journal.StageWithRequestKeyAsync(keyedId, manifest, Guid.NewGuid(),
            immutableHash, fingerprint, Now)).Succeeded);
        var intent = await fixture.Journal.CreateExecutionIntentAsync(keyedId, immutableHash, Now);
        Assert.IsTrue(intent.Succeeded);
        Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(keyedId,
            HorseIdentityMigrationExecutionPhase.Preparing, Now)).Succeeded);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await fixture.Journal.AdvanceExecutionIntentAsync(keyedId,
                HorseIdentityMigrationExecutionPhase.BackedUp, Now)).Reason);
    }

    [TestMethod]
    public async Task OnlyOneNonterminalExecutionIntentCanBeActiveAtATime()
    {
        using var fixture = new Fixture();
        var firstManifest = Manifest(11);
        var firstFingerprint = new string('1', 64);
        var firstHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(firstManifest, firstFingerprint);
        var firstId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageWithRequestKeyAsync(firstId, firstManifest, Guid.NewGuid(),
            firstHash, firstFingerprint, Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(firstId, firstHash, Now)).Succeeded);

        var secondManifest = Manifest(12);
        var secondFingerprint = new string('2', 64);
        var secondHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(secondManifest, secondFingerprint);
        var secondId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageWithRequestKeyAsync(secondId, secondManifest, Guid.NewGuid(),
            secondHash, secondFingerprint, Now)).Succeeded);
        var secondIntent = await fixture.Journal.CreateExecutionIntentAsync(secondId, secondHash, Now);

        Assert.AreEqual(HorseIdentityMigrationJournalReason.OwnershipBusy, secondIntent.Reason);
        Assert.AreEqual(firstId, (await fixture.Journal.GetActiveExecutionIntentAsync())?.OperationId);
    }

    [TestMethod]
    public async Task UnsafeOperationStatesCannotAdvanceAnIntentIntoApplying()
    {
        using var fixture = new Fixture();
        var positiveId = Guid.NewGuid();
        var positiveManifest = Manifest(3);
        var positiveFingerprint = new string('F', 64);
        var positiveHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(positiveManifest,
            positiveFingerprint);
        Assert.IsTrue((await fixture.Journal.StageWithRequestKeyAsync(positiveId, positiveManifest, Guid.NewGuid(),
            positiveHash, positiveFingerprint, Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(positiveId, positiveHash, Now)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
        })
            Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(positiveId, phase, Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(positiveId,
            HorseIdentityMigrationExecutionPhase.Applying, Now)).Succeeded);
        Assert.IsFalse(await fixture.Journal.CanBeginReadonlyVerificationAsync(positiveId));
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE migration_operations SET status = $status, reason = $reason, terminal_utc = $terminal WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$status", (int)HorseIdentityMigrationOperationStatus.Succeeded);
            command.Parameters.AddWithValue("$reason", (int)HorseIdentityMigrationJournalReason.None);
            command.Parameters.AddWithValue("$terminal", Now.ToString("O"));
            command.Parameters.AddWithValue("$id", positiveId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(positiveId,
            HorseIdentityMigrationExecutionPhase.DataSucceeded, Now)).Succeeded);
        Assert.IsTrue(await fixture.Journal.CanBeginReadonlyVerificationAsync(positiveId));
        Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(positiveId,
            HorseIdentityMigrationExecutionPhase.Verifying, Now)).Succeeded);
        Assert.IsTrue(await fixture.Journal.CanBeginReadonlyVerificationAsync(positiveId));
        Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(positiveId,
            HorseIdentityMigrationExecutionPhase.Verified, Now)).Succeeded);

        var operationId = Guid.NewGuid();
        var requestKey = Guid.NewGuid();
        var manifest = Manifest(4);
        var fingerprint = new string('G', 64);
        var hash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        Assert.IsTrue((await fixture.Journal.StageWithRequestKeyAsync(operationId, manifest, requestKey, hash,
            fingerprint, Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(operationId, hash, Now)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
        })
            Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(operationId, phase, Now)).Succeeded);

        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE migration_operations SET status = $status, reason = $reason WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$status", (int)HorseIdentityMigrationOperationStatus.PartialUnverified);
            command.Parameters.AddWithValue("$reason", (int)HorseIdentityMigrationJournalReason.OperationInterrupted);
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var applying = await fixture.Journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Applying, Now);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation, applying.Reason);
        Assert.AreEqual(HorseIdentityMigrationExecutionPhase.BackedUp, applying.Intent!.Phase);

        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE migration_operations SET status = $status, reason = $reason, terminal_utc = $terminal WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$status", (int)HorseIdentityMigrationOperationStatus.Succeeded);
            command.Parameters.AddWithValue("$reason", (int)HorseIdentityMigrationJournalReason.None);
            command.Parameters.AddWithValue("$terminal", Now.ToString("O"));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await fixture.Journal.AdvanceExecutionIntentAsync(operationId,
                HorseIdentityMigrationExecutionPhase.Applying, Now)).Reason);

        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE migration_operations SET status = $status, reason = $reason, terminal_utc = NULL WHERE operation_id = $id; UPDATE migration_request_keys SET immutable_request_hash = $hash WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$status", (int)HorseIdentityMigrationOperationStatus.Interrupted);
            command.Parameters.AddWithValue("$reason", (int)HorseIdentityMigrationJournalReason.OperationInterrupted);
            command.Parameters.AddWithValue("$hash", new string('H', 64));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
        Assert.AreEqual(HorseIdentityMigrationJournalReason.StaleOperation,
            (await fixture.Journal.AdvanceExecutionIntentAsync(operationId,
                HorseIdentityMigrationExecutionPhase.Applying, Now)).Reason);
    }

    [TestMethod]
    public async Task StartupGuardInitializesOnceAndFailsClosedWhenEitherDurableFileDisappears()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(Now)).State);
        Assert.IsTrue(File.Exists(fixture.Journal.InitializationMarkerPath));
        Assert.IsTrue(File.Exists(fixture.Journal.DatabasePath));
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(Now)).State);

        File.Delete(fixture.Journal.DatabasePath);
        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
            (await fixture.Journal.InspectStartupGuardAsync(Now)).State,
            "A durable initialization marker plus a missing journal must not be treated as first install.");
    }

    [TestMethod]
    public async Task StartupGuardPreservesStagedWorkButMarksUnownedRunningAsInterrupted()
    {
        using var fixture = new Fixture();
        var stagedId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(stagedId, Manifest(1), Now)).Succeeded);
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(Now)).State);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged,
            (await fixture.Journal.GetStatusAsync(stagedId))!.Status);

        var runningId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(runningId, Manifest(2), Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.ClaimWorkflowAsync(runningId, Now)).Succeeded);
        var ownerMarker = Path.Combine(fixture.Directory, "startup-guard-owner-ready");
        using var owner = StartChild("hold", fixture.Directory, runningId, ownerMarker);
        try
        {
            await WaitForMarkerAsync(owner, ownerMarker, TimeSpan.FromSeconds(30));
            Assert.AreEqual(HorseIdentityMigrationOperationStatus.Running,
                (await fixture.Journal.GetStatusAsync(runningId))!.Status);
        }
        finally
        {
            await StopOwnedProcessAsync(owner);
        }

        var guard = await fixture.Journal.InspectStartupGuardAsync(Now.AddMinutes(1));
        Assert.AreEqual(HorseIdentityMigrationStartupState.Interrupted, guard.State);
        Assert.AreEqual(runningId, guard.OperationId);
        var interrupted = await fixture.Journal.GetStatusAsync(runningId);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Interrupted, interrupted!.Status);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged,
            (await fixture.Journal.GetStatusAsync(stagedId))!.Status);
    }

    [TestMethod]
    public async Task StartupGuardNeverInterruptsLiveOwnerAndRejectsMissingMarker()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(1), Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.ClaimWorkflowAsync(operationId, Now)).Succeeded);
        await using (var lease = await fixture.Journal.TryAcquireExecutionAsync(operationId, Now))
        {
            Assert.IsNotNull(lease);
            Assert.AreEqual(HorseIdentityMigrationStartupState.OwnerActive,
                (await fixture.Journal.InspectStartupGuardAsync(Now)).State);
            Assert.AreEqual(HorseIdentityMigrationOperationStatus.Running,
                (await fixture.Journal.GetStatusAsync(operationId))!.Status);
        }

        using var missingMarker = new Fixture();
        await missingMarker.Journal.StageAsync(Guid.NewGuid(), Manifest(1), Now);
        File.Delete(missingMarker.Journal.InitializationMarkerPath);
        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
            (await missingMarker.Journal.InspectStartupGuardAsync(Now)).State);
    }

    [TestMethod]
    public async Task ClaimedStagedRequestIsReboundToCanonicalValidatedManifest()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(1), Now)).Succeeded);
        Assert.IsNull(await fixture.Journal.GetClaimedStagedRequestAsync(operationId));
        Assert.IsTrue((await fixture.Journal.ClaimWorkflowAsync(operationId, Now)).Succeeded);

        var staged = await fixture.Journal.GetClaimedStagedRequestAsync(operationId);
        Assert.IsNotNull(staged);
        Assert.AreEqual(operationId, staged.OperationId);
        Assert.IsTrue(HorseIdentityMigrationManifestPolicy.Validate(staged.Manifest).IsValid);
    }

    [TestMethod]
    public async Task OversizedPersistedStagedRequestIsRejectedBeforeJsonMaterialization()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(1), Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.ClaimWorkflowAsync(operationId, Now)).Succeeded);
        var oversizedJson = "\"" + new string('x', HorseIdentityMigrationJournal.MaximumRequestBytes) + "\"";
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE migration_operations SET request_json = $json WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$json", oversizedJson);
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        await ExpectExceptionAsync<InvalidDataException>(() => fixture.Journal.GetClaimedStagedRequestAsync(operationId));
    }

    [TestMethod]
    public async Task CorruptPersistedStatusOrReasonFailsClosed()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(1), Now)).Succeeded);

        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE migration_operations SET status = $status, reason = $reason WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$status", (int)HorseIdentityMigrationOperationStatus.Succeeded);
            command.Parameters.AddWithValue("$reason", 999);
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        await ExpectExceptionAsync<InvalidDataException>(() => fixture.Journal.GetStatusAsync(operationId));
    }

    [TestMethod]
    public async Task SucceededStatusWithoutExactVerifiedPageSetFailsStatusAndStartupGuard()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var lease = await StartClaimedAsync(fixture, operationId);
        var evidence = Evidence(0);
        Assert.IsTrue((await fixture.Journal.RegisterRequiredPagesAsync(operationId, [Prepared(evidence)], Now)).Succeeded);
        await AdvanceToVerifiedAsync(fixture.Journal, operationId, evidence);
        await lease.CompleteAsync(Now);
        await lease.DisposeAsync();

        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM migration_required_pages WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        await ExpectExceptionAsync<InvalidDataException>(() => fixture.Journal.GetStatusAsync(operationId));
        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
            (await fixture.Journal.InspectStartupGuardAsync(Now)).State);
    }

    [TestMethod]
    public async Task DefinedButInconsistentRunningReasonPairFailsClosed()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(1), Now)).Succeeded);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE migration_operations SET status = $running, reason = $interrupted WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$running", (int)HorseIdentityMigrationOperationStatus.Running);
            command.Parameters.AddWithValue("$interrupted", (int)HorseIdentityMigrationJournalReason.OperationInterrupted);
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        await ExpectExceptionAsync<InvalidDataException>(() => fixture.Journal.GetStatusAsync(operationId));
        Assert.AreEqual(HorseIdentityMigrationStartupState.Corrupt,
            (await fixture.Journal.InspectStartupGuardAsync(Now)).State);
    }

    [TestMethod]
    public async Task SqliteRollbackJournalPeakFitsConfigured64MiBBound()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(1), Now)).Succeeded);

        await using var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sqlite_version() || ' ' || sqlite_source_id();";
            TestContext.WriteLine("Loaded SQLite: " + await command.ExecuteScalarAsync());
        }

        var pageSize = await ScalarInt64Async(connection, "PRAGMA page_size;");
        var maximumPages = HorseIdentityMigrationJournal.ComputeMaximumPageCount(
            HorseIdentityMigrationJournal.MaximumJournalBytes, 136 * 1024,
            File.Exists(fixture.Journal.LockPath) ? new FileInfo(fixture.Journal.LockPath).Length : 0, pageSize);
        await using (var ceiling = connection.CreateCommand())
        {
            ceiling.CommandText = $"PRAGMA max_page_count = {maximumPages};";
            await ceiling.ExecuteScalarAsync();
        }
        maximumPages = await ScalarInt64Async(connection, "PRAGMA max_page_count;");
        Assert.IsTrue(maximumPages > 0);
        Assert.IsTrue(WorstCaseRollbackFootprint(maximumPages, pageSize) + 136 * 1024
            <= HorseIdentityMigrationJournal.MaximumJournalBytes);

        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "UPDATE migration_operations SET results_json = $json WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$json", "\"" + new string('x', HorseIdentityMigrationJournal.MaximumResultBytes - 2) + "\"");
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
        }

        var journalPath = fixture.Journal.DatabasePath + "-journal";
        Assert.IsTrue(File.Exists(journalPath), "A real DELETE-mode rollback sidecar must be observed before commit.");
        var header = new byte[24];
        await using (var journal = new FileStream(journalPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
            await journal.ReadExactlyAsync(header);
        var sectorSize = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        Assert.IsTrue(sectorSize is > 0 and <= 65_536, $"Unexpected SQLite rollback sector size {sectorSize}.");
        TestContext.WriteLine($"Observed rollback sector={sectorSize}; max-pages={maximumPages}; page-size={pageSize}.");
        var files = new[] { fixture.Journal.DatabasePath, journalPath, fixture.Journal.DatabasePath + "-wal",
            fixture.Journal.DatabasePath + "-shm", fixture.Journal.LockPath };
        var peak = files.Where(File.Exists).Sum(path => new FileInfo(path).Length);
        Assert.IsTrue(peak <= HorseIdentityMigrationJournal.MaximumJournalBytes,
            $"Observed rollback transaction peak {peak} exceeded the 64 MiB bound.");
        await transaction.CommitAsync();
    }

    [TestMethod]
    public async Task StatusReadDoesNotClaimOrInterruptStagedOperation()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        await fixture.Journal.StageAsync(operationId, Manifest(1), Now);

        var status = await fixture.Journal.GetStatusAsync(operationId);
        Assert.IsNotNull(status);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged, status.Status);

        Assert.IsFalse(File.Exists(fixture.Journal.LockPath), "A status read must not create the execution lock.");
        var owner = await fixture.Journal.TryAcquireExecutionAsync(operationId, Now);
        Assert.IsNull(owner, "Staged work must not acquire execution ownership without an explicit claim.");
    }

    [TestMethod]
    public async Task CompletionRequiresEveryExplicitRequiredPageToBeVerified()
    {
        using var fixture = new Fixture();
        var lease = await StartClaimedAsync(fixture, Guid.NewGuid());
        await using (lease)
        {
            var first = Evidence(0);
            var second = Evidence(1);
            var required = new[] { Prepared(first), Prepared(second) };
            Assert.IsTrue((await fixture.Journal.RegisterRequiredPagesAsync(lease.OperationId, required, Now)).Succeeded);
            Assert.AreEqual(HorseIdentityMigrationJournalReason.RequestConflict,
                (await fixture.Journal.RecordPageCheckpointAsync(lease.OperationId, Prepared(Evidence(2)), Now)).Reason);

            await AdvanceToVerifiedAsync(fixture.Journal, lease.OperationId, first);
            await ExpectExceptionAsync<InvalidOperationException>(() => lease.CompleteAsync(Now));
            await AdvanceToVerifiedAsync(fixture.Journal, lease.OperationId, second);
            await lease.CompleteAsync(Now);
        }

        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await fixture.Journal.GetStatusAsync(lease.OperationId))!.Status);
    }

    [TestMethod]
    public async Task SupervisedCompletionAtomicallyMakesSucceededIntentReadonlyEligible()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var manifest = Manifest(7);
        var fingerprint = new string('B', 64);
        var immutableHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        Assert.IsTrue((await fixture.Journal.StageWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
            immutableHash, fingerprint, Now)).Succeeded);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(operationId, immutableHash, Now)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
            HorseIdentityMigrationExecutionPhase.Applying,
        })
            Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(operationId, phase, Now)).Succeeded);

        Assert.IsTrue((await fixture.Journal.ClaimWorkflowAsync(operationId, Now)).Succeeded);
        await using var lease = await fixture.Journal.TryAcquireExecutionAsync(operationId, Now)
            ?? throw new AssertFailedException("Expected the explicitly claimed migration lease.");
        var evidence = Evidence(7);
        Assert.IsTrue((await fixture.Journal.RegisterRequiredPagesAsync(operationId, [Prepared(evidence)], Now)).Succeeded);
        await AdvanceToVerifiedAsync(fixture.Journal, operationId, evidence);

        await lease.CompleteAsync(Now);

        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await fixture.Journal.GetStatusAsync(operationId))!.Status);
        Assert.AreEqual(HorseIdentityMigrationExecutionPhase.DataSucceeded,
            (await fixture.Journal.GetExecutionIntentAsync(operationId))!.Phase);
        Assert.IsTrue(await fixture.Journal.CanBeginReadonlyVerificationAsync(operationId));
    }

    [TestMethod]
    public async Task PartialEvidenceSurvivesCapacityFailureAndDispose()
    {
        using var fixture = new Fixture(maximumBytes: 2 * 1024 * 1024, reserveBytes: 64 * 1024);
        var lease = await StartClaimedAsync(fixture, Guid.NewGuid());
        var evidence = Evidence(0);
        await fixture.Journal.RegisterRequiredPagesAsync(lease.OperationId, [Prepared(evidence)], Now);
        await fixture.Journal.RecordPageCheckpointAsync(lease.OperationId, Prepared(evidence), Now);

        var largeResults = Enumerable.Repeat(
            new HorseIdentityMigrationJournalResultItem(HorseIdentityMigrationResultCode.PlanIncomplete), 30_000).ToArray();
        var capacity = await fixture.Journal.SetResultsAsync(lease.OperationId, largeResults, Now);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.CapacityExceeded, capacity.Reason);

        await lease.DisposeAsync();
        var stored = await fixture.Journal.GetPageCheckpointAsync(lease.OperationId, evidence);
        var status = await fixture.Journal.GetStatusAsync(lease.OperationId);
        Assert.IsNotNull(stored);
        Assert.AreEqual(HorseIdentityMigrationPageState.Prepared, stored.State);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.PartialUnverified, status!.Status);
    }

    [TestMethod]
    public async Task TerminalRetentionPurgesEligibleSuccessButNeverPartialEvidence()
    {
        using var fixture = new Fixture();
        var oldSuccessId = Guid.NewGuid();
        var successLease = await StartClaimedAsync(fixture, oldSuccessId, Now.AddDays(-31));
        await fixture.Journal.RegisterRequiredPagesAsync(oldSuccessId, [Prepared(Evidence(0))], Now.AddDays(-31));
        await AdvanceToVerifiedAsync(fixture.Journal, oldSuccessId, Evidence(0), Now.AddDays(-31));
        await successLease.CompleteAsync(Now.AddDays(-31));
        await successLease.DisposeAsync();

        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO migration_execution_intents (operation_id, immutable_request_hash, created_utc, updated_utc, phase) VALUES ($id, $hash, $created, $updated, $phase);";
            command.Parameters.AddWithValue("$id", oldSuccessId.ToString("D"));
            command.Parameters.AddWithValue("$hash", new string('A', 64));
            command.Parameters.AddWithValue("$created", Now.AddDays(-31).ToString("O"));
            command.Parameters.AddWithValue("$updated", Now.AddDays(-31).ToString("O"));
            command.Parameters.AddWithValue("$phase", (int)HorseIdentityMigrationExecutionPhase.DataSucceeded);
            await command.ExecuteNonQueryAsync();
        }

        var eligibleSuccessId = Guid.NewGuid();
        var eligibleLease = await StartClaimedAsync(fixture, eligibleSuccessId, Now.AddDays(-31));
        await fixture.Journal.RegisterRequiredPagesAsync(eligibleSuccessId, [Prepared(Evidence(2))], Now.AddDays(-31));
        await AdvanceToVerifiedAsync(fixture.Journal, eligibleSuccessId, Evidence(2), Now.AddDays(-31));
        await eligibleLease.CompleteAsync(Now.AddDays(-31));
        await eligibleLease.DisposeAsync();

        var partialId = Guid.NewGuid();
        var partialLease = await StartClaimedAsync(fixture, partialId, Now.AddDays(-31));
        await fixture.Journal.RegisterRequiredPagesAsync(partialId, [Prepared(Evidence(1))], Now.AddDays(-31));
        await fixture.Journal.RecordPageCheckpointAsync(partialId, Prepared(Evidence(1)), Now.AddDays(-31));
        await partialLease.MarkPartialAsync(Now.AddDays(-31));
        await partialLease.DisposeAsync();

        var newId = Guid.NewGuid();
        await fixture.Journal.StageAsync(newId, Manifest(2), Now);
        Assert.IsNull(await fixture.Journal.GetStatusAsync(eligibleSuccessId));
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await fixture.Journal.GetStatusAsync(oldSuccessId))!.Status);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.PartialUnverified,
            (await fixture.Journal.GetStatusAsync(partialId))!.Status);
        Assert.IsNotNull(await fixture.Journal.GetPageCheckpointAsync(partialId, Evidence(1)));
    }

    [TestMethod]
    public async Task RejectsJournalSourceCollisionAndReparseDirectory()
    {
        using var fixture = new Fixture();
        var sourcePath = Path.Combine(fixture.Directory, "source.db");
        await ExpectExceptionAsync<InvalidOperationException>(() =>
            new HorseIdentityMigrationJournal(fixture.Directory, [fixture.Journal.DatabasePath])
                .StageAsync(Guid.NewGuid(), Manifest(1), Now));

        var linkPath = fixture.Directory + "-link";
        try
        {
            if (OperatingSystem.IsLinux())
                Directory.CreateSymbolicLink(linkPath, fixture.Directory);
            else if (!await TryCreateJunctionAsync(linkPath, fixture.Directory))
                Assert.Inconclusive("This Windows environment does not permit creating a directory junction.");
            var linked = new HorseIdentityMigrationJournal(linkPath, [sourcePath]);
            await ExpectExceptionAsync<InvalidOperationException>(() => linked.StageAsync(Guid.NewGuid(), Manifest(1), Now));
        }
        finally
        {
            if (Directory.Exists(linkPath)) Directory.Delete(linkPath);
        }
    }

    [TestMethod]
    public async Task FilteredChildProcessesProveBusyOwnershipAndKilledOwnerRecovery()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        await fixture.Journal.StageAsync(operationId, Manifest(1), Now);
        await fixture.Journal.ClaimWorkflowAsync(operationId, Now);

        var readyPath = Path.Combine(fixture.Directory, "owner-ready");
        using var owner = StartChild("hold", fixture.Directory, operationId, readyPath);
        try
        {
            await WaitForMarkerAsync(owner, readyPath, TimeSpan.FromSeconds(30));
            Assert.AreEqual(HorseIdentityMigrationOperationStatus.Running,
                (await fixture.Journal.GetStatusAsync(operationId))!.Status);

            var busyPath = Path.Combine(fixture.Directory, "busy-result");
            using var contender = StartChild("busy", fixture.Directory, operationId, busyPath);
            try
            {
                await WaitForProcessAsync(contender, TimeSpan.FromSeconds(30));
                Assert.AreEqual("busy", await File.ReadAllTextAsync(busyPath));
            }
            finally
            {
                await StopOwnedProcessAsync(contender);
            }
        }
        finally
        {
            await StopOwnedProcessAsync(owner);
        }

        Assert.IsNull(await fixture.Journal.TryAcquireExecutionAsync(operationId, Now.AddMinutes(1)));
        var interrupted = await fixture.Journal.GetStatusAsync(operationId);
        Assert.IsNotNull(interrupted);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Interrupted, interrupted.Status);
        Assert.AreEqual(HorseIdentityMigrationJournalReason.OperationInterrupted, interrupted.Reason);
    }

    [TestMethod]
    public async Task FilteredChildStallIsBoundedAndItsProcessTreeIsStopped()
    {
        using var fixture = new Fixture();
        var stallPath = Path.Combine(fixture.Directory, "never-ready");
        using var stalled = StartChild("stall", fixture.Directory, Guid.NewGuid(), stallPath);
        try
        {
            await ExpectExceptionAsync<TimeoutException>(() => WaitForMarkerAsync(stalled, stallPath,
                TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            await StopOwnedProcessAsync(stalled);
        }

        Assert.IsTrue(stalled.HasExited);
    }

    [TestMethod]
    public async Task TimedOutChildWaitStopsTheOwnedProcessTree()
    {
        using var fixture = new Fixture();
        using var stalled = StartChild("stall", fixture.Directory, Guid.NewGuid(),
            Path.Combine(fixture.Directory, "process-timeout"));
        try
        {
            await ExpectExceptionAsync<OperationCanceledException>(() =>
                WaitForProcessAsync(stalled, TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            await StopOwnedProcessAsync(stalled);
        }

        Assert.IsTrue(stalled.HasExited);
    }

    [TestMethod]
    public async Task TermIgnoringChildIsKilledAndReapedWithItsOwnedProcessGroup()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var fixture = new Fixture();
        var readyPath = Path.Combine(fixture.Directory, "term-ignored");
        using var stubborn = StartChild("ignore-term", fixture.Directory, Guid.NewGuid(), readyPath);
        var escalatedToKill = false;
        try
        {
            await WaitForMarkerAsync(stubborn, readyPath, TimeSpan.FromSeconds(30));
            escalatedToKill = await StopOwnedProcessAsync(stubborn);
        }
        finally
        {
            await StopOwnedProcessAsync(stubborn);
        }

        Assert.IsTrue(escalatedToKill, $"The TERM-ignoring child must force KILL escalation. {GetChildOutput(stubborn)}");
        Assert.IsTrue(stubborn.HasExited, $"The outer child must be reaped. {GetChildOutput(stubborn)}");
        Assert.IsFalse(await IsProcessGroupAliveAsync(stubborn.Id),
            $"TERM escalation must leave no process in the owned group. {GetChildOutput(stubborn)}");
    }

    [TestMethod]
    public async Task ChildProcessEntryPointRunsOnlyWhenExplicitlySelectedByParent()
    {
        var mode = Environment.GetEnvironmentVariable(ChildModeVariable);
        if (string.IsNullOrEmpty(mode)) return;

        var directory = Environment.GetEnvironmentVariable(ChildDirectoryVariable)
            ?? throw new InvalidOperationException("Missing child directory.");
        var operationId = Guid.Parse(Environment.GetEnvironmentVariable(ChildOperationVariable)
            ?? throw new InvalidOperationException("Missing child operation id."));
        var marker = Environment.GetEnvironmentVariable(ChildReadyVariable)
            ?? throw new InvalidOperationException("Missing child marker path.");

        if (mode == "ignore-term")
        {
            var stubbornChild = new Process
            {
                StartInfo = new ProcessStartInfo("/bin/sh")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList =
                    {
                        "-c",
                        "trap '' TERM; printf ready > \"$1\"; while :; do sleep 1; done",
                        "hrp-owned-term-ignorer",
                        marker,
                    },
                },
                EnableRaisingEvents = true,
            };
            if (!stubbornChild.Start()) throw new InvalidOperationException("The TERM-ignoring helper did not start.");
            await stubbornChild.WaitForExitAsync();
            return;
        }

        var journal = new HorseIdentityMigrationJournal(directory, []);

        if (mode == "stall")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }

        var lease = await journal.TryAcquireExecutionAsync(operationId, DateTimeOffset.UtcNow);
        if (mode == "busy")
        {
            await File.WriteAllTextAsync(marker, lease is null ? "busy" : "acquired");
            if (lease is not null) await lease.DisposeAsync();
            return;
        }

        if (mode != "hold" || lease is null)
            throw new InvalidOperationException("The child could not acquire the requested operation lock.");
        await using (lease)
        {
            await File.WriteAllTextAsync(marker, "ready");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }

    private static async Task<HorseIdentityMigrationJournal.HorseIdentityMigrationExecutionLease> StartClaimedAsync(
        Fixture fixture, Guid operationId, DateTimeOffset? now = null)
    {
        var timestamp = now ?? Now;
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(1), timestamp)).Succeeded);
        Assert.IsTrue((await fixture.Journal.ClaimWorkflowAsync(operationId, timestamp)).Succeeded);
        var lease = await fixture.Journal.TryAcquireExecutionAsync(operationId, timestamp);
        Assert.IsNotNull(lease);
        return lease!;
    }

    private static async Task AdvanceToVerifiedAsync(HorseIdentityMigrationJournal journal, Guid operationId,
        TypedHorseIdentityPageEvidence evidence, DateTimeOffset? now = null)
    {
        var timestamp = now ?? Now;
        Assert.IsTrue((await journal.RecordPageCheckpointAsync(operationId,
            new(evidence, Hash("membership" + evidence.Table + evidence.FirstKey), HorseIdentityMigrationPageState.Prepared), timestamp)).Succeeded);
        Assert.IsTrue((await journal.RecordPageCheckpointAsync(operationId,
            new(evidence, Hash("membership" + evidence.Table + evidence.FirstKey), HorseIdentityMigrationPageState.CommittedUnverified), timestamp)).Succeeded);
        Assert.IsTrue((await journal.RecordPageCheckpointAsync(operationId,
            new(evidence, Hash("membership" + evidence.Table + evidence.FirstKey), HorseIdentityMigrationPageState.Verified), timestamp)).Succeeded);
    }

    private static HorseIdentityMigrationPageCheckpoint Prepared(TypedHorseIdentityPageEvidence evidence) =>
        new(evidence, Hash("membership" + evidence.Table + evidence.FirstKey), HorseIdentityMigrationPageState.Prepared);

    private static TypedHorseIdentityPageEvidence Evidence(int page) => new(
        MigrationStoreSurface.EventStore,
        "EventEntity",
        3,
        Hash("before" + page),
        Hash("after" + page),
        (page * 128).ToString(System.Globalization.CultureInfo.InvariantCulture),
        (page * 128 + 2).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string?[]> ReadOperationRowAsync(SqliteConnection connection, Guid operationId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_hash, request_json, status, reason, created_utc, updated_utc, terminal_utc, results_json, workflow_claimed FROM migration_operations WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        var row = new string?[reader.FieldCount];
        for (var index = 0; index < row.Length; index++)
            row[index] = reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index),
                System.Globalization.CultureInfo.InvariantCulture);
        return row;
    }

    private static long WorstCaseRollbackFootprint(long pages, long pageSize) =>
        pages * pageSize + pages * (pageSize + 8) + (pages + 1) * 65_536 + 24;

    private static async Task<TException> ExpectExceptionAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return exception;
        }
        Assert.Fail($"Expected {typeof(TException).Name}.");
        throw new InvalidOperationException("Unreachable after a failed assertion.");
    }

    private static HorseIdentityMigrationManifest Manifest(int index) => new(
    [
        new HorseIdentityMigrationManifestEntry(
            $"horse-00000000-0000-0000-0000-{index:000000000001}",
            $"jra-horse:{2024100800 + index}", true),
    ]);

    private static Process StartChild(string mode, string directory, Guid operationId, string markerPath)
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "tests", "HorseRacingPrediction.Api.Tests",
            "HorseRacingPrediction.Api.Tests.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FindRepositoryRoot(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "test", projectPath, "-c", "Release", "--no-build", "--no-restore",
            "--filter", "FullyQualifiedName~HorseIdentityMigrationJournalTests.ChildProcessEntryPoint",
        }) startInfo.ArgumentList.Add(argument);
        startInfo.Environment[ChildModeVariable] = mode;
        startInfo.Environment[ChildDirectoryVariable] = directory;
        startInfo.Environment[ChildOperationVariable] = operationId.ToString("D");
        startInfo.Environment[ChildReadyVariable] = markerPath;
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        if (OperatingSystem.IsLinux())
        {
            var groupedStartInfo = new ProcessStartInfo("setsid")
            {
                WorkingDirectory = startInfo.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { startInfo.FileName }.Concat(startInfo.ArgumentList))
                groupedStartInfo.ArgumentList.Add(argument);
            foreach (var (name, value) in startInfo.Environment) groupedStartInfo.Environment[name] = value;
            startInfo = groupedStartInfo;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var capturedOutput = new BoundedProcessOutput(32 * 1024);
        ChildOutput.Add(process, capturedOutput);
        process.OutputDataReceived += (_, args) => capturedOutput.Append(args.Data);
        process.ErrorDataReceived += (_, args) => capturedOutput.Append(args.Data);
        if (!process.Start()) throw new InvalidOperationException("The filtered journal child did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();
        return process;
    }

    private static async Task<bool> TryCreateJunctionAsync(string path, string target)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path $env:HRP_JOURNAL_JUNCTION_PATH -Target $env:HRP_JOURNAL_JUNCTION_TARGET | Out-Null");
        startInfo.Environment["HRP_JOURNAL_JUNCTION_PATH"] = path;
        startInfo.Environment["HRP_JOURNAL_JUNCTION_TARGET"] = target;
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 && Directory.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cleanupTimeout.Token).ConfigureAwait(false);
            return false;
        }
    }

    private static async Task WaitForMarkerAsync(Process process, string markerPath, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!deadline.IsCancellationRequested)
        {
            if (File.Exists(markerPath)) return;
            if (process.HasExited)
                throw new InvalidOperationException($"The filtered journal child exited before its readiness marker ({process.ExitCode}).\n{GetChildOutput(process)}");
            try
            {
                await Task.Delay(50, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                throw new TimeoutException("The filtered journal child did not reach its bounded readiness marker.");
            }
        }
        throw new TimeoutException("The filtered journal child did not reach its bounded readiness marker.");
    }

    private static async Task WaitForProcessAsync(Process process, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The filtered journal child failed with exit code {process.ExitCode}.\n{GetChildOutput(process)}");
    }

    private static async Task<bool> StopOwnedProcessAsync(Process process)
    {
        if (OperatingSystem.IsLinux())
        {
            await SignalProcessGroupAsync(process.Id, "TERM").ConfigureAwait(false);
            var escalatedToKill = false;
            if (!await WaitForProcessGroupExitAsync(process.Id, TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            {
                escalatedToKill = true;
                await SignalProcessGroupAsync(process.Id, "KILL").ConfigureAwait(false);
                if (!await WaitForProcessGroupExitAsync(process.Id, TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                    throw new TimeoutException($"Owned Linux process group {process.Id} remained alive after KILL. {GetChildOutput(process)}");
            }
            if (!process.HasExited)
            {
                using var outerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(outerTimeout.Token).ConfigureAwait(false);
            }
            return escalatedToKill;
        }
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        return false;
    }

    private static string GetChildOutput(Process process) =>
        ChildOutput.TryGetValue(process, out var output) ? output.ToString() : "(no child output captured)";

    private static async Task SignalProcessGroupAsync(int processGroupId, string signal)
    {
        using var helper = Process.Start(new ProcessStartInfo("/bin/kill")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { $"-{signal}", "--", $"-{processGroupId}" },
        }) ?? throw new InvalidOperationException("Could not start the Linux process-group signal helper.");
        helper.BeginOutputReadLine();
        helper.BeginErrorReadLine();
        await WaitForHelperAsync(helper).ConfigureAwait(false);
        if (helper.ExitCode != 0 && await IsProcessGroupAliveAsync(processGroupId).ConfigureAwait(false))
            throw new InvalidOperationException($"Could not send {signal} to owned process group {processGroupId}.");
    }

    private static async Task<bool> WaitForProcessGroupExitAsync(int processGroupId, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!deadline.IsCancellationRequested && await IsProcessGroupAliveAsync(processGroupId).ConfigureAwait(false))
        {
            try { await Task.Delay(50, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
        }
        return !await IsProcessGroupAliveAsync(processGroupId).ConfigureAwait(false);
    }

    private static async Task<bool> IsProcessGroupAliveAsync(int processGroupId)
    {
        using var helper = Process.Start(new ProcessStartInfo("/bin/kill")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "-0", "--", $"-{processGroupId}" },
        }) ?? throw new InvalidOperationException("Could not start the Linux process-group check helper.");
        helper.BeginOutputReadLine();
        helper.BeginErrorReadLine();
        await WaitForHelperAsync(helper).ConfigureAwait(false);
        return helper.ExitCode == 0;
    }

    private static async Task WaitForHelperAsync(Process helper)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!helper.HasExited) helper.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await helper.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            throw new TimeoutException("The bounded Linux process-group helper exceeded five seconds and was terminated.");
        }
    }

    private sealed class BoundedProcessOutput(int maximumLength)
    {
        private readonly StringBuilder _text = new(maximumLength);
        private readonly object _sync = new();

        internal void Append(string? line)
        {
            if (line is null) return;
            lock (_sync)
            {
                if (_text.Length >= maximumLength) return;
                _text.Append(line.AsSpan(0, Math.Min(line.Length, maximumLength - _text.Length))).AppendLine();
            }
        }

        public override string ToString()
        {
            lock (_sync) return _text.ToString();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture(long maximumBytes = HorseIdentityMigrationJournal.MaximumJournalBytes,
            long reserveBytes = 136 * 1024)
        {
            Directory = Path.Combine(Path.GetTempPath(), "hrp-horse-identity-journal-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Journal = new HorseIdentityMigrationJournal(Directory,
                [Path.Combine(Directory, "event.db"), Path.Combine(Directory, "collection.db"),
                    Path.Combine(Directory, "schedule.db"), Path.Combine(Directory, "queue.db")],
                maximumBytes, reserveBytes);
        }

        internal string Directory { get; }
        internal HorseIdentityMigrationJournal Journal { get; }
        internal string LastOperationId { get; set; } = Guid.Empty.ToString("D");

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

internal static class HistoricalHorseIdentityMigrationJournalFixture
{
    internal static async Task CreateAsync(string databasePath, string markerPath, string lockPath,
        Guid? operationId = null, HorseIdentityMigrationManifest? manifest = null, DateTimeOffset? createdAt = null)
    {
        if ((operationId is null) != (manifest is null))
            throw new ArgumentException("A historical core row requires both an operation ID and manifest.");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await File.WriteAllTextAsync(markerPath, "horse-identity-migration-journal-v1", Encoding.ASCII);
        await File.WriteAllBytesAsync(lockPath, []);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        await connection.OpenAsync();
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA journal_mode = DELETE;
                PRAGMA synchronous = FULL;
                PRAGMA foreign_keys = ON;
                CREATE TABLE migration_operations (
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
                CREATE TABLE migration_pages (
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
                CREATE TABLE migration_required_pages (
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
            await schema.ExecuteNonQueryAsync();
        }

        if (manifest is null || operationId is null) return;
        var validated = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        if (!validated.IsValid) throw new InvalidDataException("The historical fixture manifest must be valid.");
        var json = JsonSerializer.Serialize(validated.Mappings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var timestamp = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O");
        await using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO migration_operations (operation_id, request_hash, request_json, status, reason, created_utc, updated_utc, terminal_utc, results_json, workflow_claimed) VALUES ($id, $hash, $json, 0, 0, $created, $updated, NULL, '[]', 0);";
        insert.Parameters.AddWithValue("$id", operationId.Value.ToString("D"));
        insert.Parameters.AddWithValue("$hash", hash);
        insert.Parameters.AddWithValue("$json", json);
        insert.Parameters.AddWithValue("$created", timestamp);
        insert.Parameters.AddWithValue("$updated", timestamp);
        await insert.ExecuteNonQueryAsync();
    }
}
