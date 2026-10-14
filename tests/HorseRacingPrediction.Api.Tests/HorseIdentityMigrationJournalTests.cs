using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using HorseRacingPrediction.Api.IdentityMigration;
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

        var partialId = Guid.NewGuid();
        var partialLease = await StartClaimedAsync(fixture, partialId, Now.AddDays(-31));
        await fixture.Journal.RegisterRequiredPagesAsync(partialId, [Prepared(Evidence(1))], Now.AddDays(-31));
        await fixture.Journal.RecordPageCheckpointAsync(partialId, Prepared(Evidence(1)), Now.AddDays(-31));
        await partialLease.MarkPartialAsync(Now.AddDays(-31));
        await partialLease.DisposeAsync();

        var newId = Guid.NewGuid();
        await fixture.Journal.StageAsync(newId, Manifest(2), Now);
        Assert.IsNull(await fixture.Journal.GetStatusAsync(oldSuccessId));
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
            if (!await TryCreateJunctionAsync(linkPath, fixture.Directory))
                Assert.Inconclusive("This Windows environment does not permit creating a directory junction; Linux CI must run the symlink rejection fixture.");
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

        var outputLimit = 32 * 1024;
        var output = new StringBuilder(outputLimit);
        void Capture(string? line)
        {
            if (line is null || output.Length >= outputLimit) return;
            var remaining = outputLimit - output.Length;
            output.Append(line.AsSpan(0, Math.Min(line.Length, remaining))).AppendLine();
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => Capture(args.Data);
        process.ErrorDataReceived += (_, args) => Capture(args.Data);
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
                throw new InvalidOperationException("The filtered journal child exited before its readiness marker.");
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
            throw new InvalidOperationException($"The filtered journal child failed with exit code {process.ExitCode}.");
    }

    private static async Task StopOwnedProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
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
