using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionCompatibleStoppedModeRestartTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PublishMutationTimeout = TimeSpan.FromSeconds(12);
    private const string PipelinePath = "/api/v2/admin/collection/pipeline-state";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(240_000)]
    public async Task DefaultMode_RestartFromConsistentBackup_RetainsExistingStartupMutators()
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "hrp-c9-stopped-restart", Guid.NewGuid().ToString("N"));
        var seedDirectory = Path.Combine(temporaryRoot, "seed-state");
        var restoredDirectory = Path.Combine(temporaryRoot, "restored-state");
        var seedDatabase = Path.Combine(seedDirectory, "collection-platform.db");
        var restoredDatabase = Path.Combine(restoredDirectory, "collection-platform.db");
        var eventStorePath = Path.Combine(temporaryRoot, "eventstore.db");
        var localApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(seedDirectory);
        Directory.CreateDirectory(restoredDirectory);
        var ownedProcesses = new List<Process>();

        try
        {
            await SeedPausedWorkAsync(seedDirectory);
            var seeded = await CaptureDatabaseSnapshotAsync(seedDatabase);
            await BackupDatabaseAsync(seedDatabase, restoredDatabase);
            var restored = await CaptureDatabaseSnapshotAsync(restoredDatabase);
            AssertSnapshotsEqual(seeded, restored, "The consistent SQLite backup must restore into a distinct path exactly.");

            var before = restored;
            var beforeEvidence = await ReadTargetEvidenceAsync(restoredDatabase);
            var firstHost = await StartAndReadPausedPipelineAsync(repositoryRoot, restoredDirectory, eventStorePath,
                temporaryRoot, localApiKey);
            ownedProcesses.Add(firstHost.Host.Process);
            var afterFirstAutoRecovery = await CaptureDatabaseSnapshotAsync(restoredDatabase);
            var firstPublishMutation = await WaitForTableMutationAsync(restoredDatabase, before,
                "collection_failure_notifications", PublishMutationTimeout);
            await StopOwnedProcessAsync(firstHost.Host.Process);
            var afterFirstStart = await CaptureDatabaseSnapshotAsync(restoredDatabase);
            var subjectMutation = HasAnyDifference(before.BusinessTables, afterFirstAutoRecovery.BusinessTables,
                "collection_requests", "collection_tasks", "collection_task_outbox");
            var afterEvidence = await ReadTargetEvidenceAsync(restoredDatabase);

            TestContext.WriteLine($"Startup business-table differences: {FormatDifferences(before.BusinessTables, afterFirstStart.BusinessTables)}");
            TestContext.WriteLine($"Startup-definition differences: {FormatDifferences(before.DefinitionTables, afterFirstStart.DefinitionTables)}");
            TestContext.WriteLine($"Startup schema metadata changed: {before.SchemaFingerprint != afterFirstStart.SchemaFingerprint}");
            TestContext.WriteLine($"Subject auto-recovery changed persisted request/task state: {subjectMutation}");
            TestContext.WriteLine($"Alert publisher changed persisted notification on first start: {firstPublishMutation}");
            TestContext.WriteLine($"Target rows before startup: {beforeEvidence}");
            TestContext.WriteLine($"Target rows after startup: {afterEvidence}");
            Assert.IsTrue(firstHost.Pipeline.IsPaused, "The default-mode API restart must preserve the persisted pause.");
            Assert.IsTrue(subjectMutation && firstPublishMutation,
                $"The unset MaintenanceMode must retain existing default startup behavior. "
                + $"Observed subjectMutation={subjectMutation}, alertPublishFailureMutation={firstPublishMutation}. "
                + $"Business table differences: {FormatDifferences(before.BusinessTables, afterFirstStart.BusinessTables)}");
        }
        finally
        {
            foreach (var process in ownedProcesses)
                await StopOwnedProcessAsync(process);
            DeleteCheckedTemporaryDirectory(temporaryRoot, "hrp-c9-stopped-restart");
        }
    }

    [TestMethod]
    [Timeout(300_000)]
    public async Task MaintenanceMode_TwoRestartsPreserveBusyWorkAndKeepsManualBackfillAvailable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "hrp-c10-maintenance-restart", Guid.NewGuid().ToString("N"));
        var seedDirectory = Path.Combine(temporaryRoot, "seed-state");
        var restoredDirectory = Path.Combine(temporaryRoot, "restored-state");
        var seedDatabase = Path.Combine(seedDirectory, "collection-platform.db");
        var restoredDatabase = Path.Combine(restoredDirectory, "collection-platform.db");
        var eventStorePath = Path.Combine(temporaryRoot, "eventstore.db");
        var localApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(seedDirectory);
        Directory.CreateDirectory(restoredDirectory);
        var ownedProcesses = new List<Process>();

        try
        {
            await SeedPausedWorkAsync(seedDirectory);
            var seeded = await CaptureDatabaseSnapshotAsync(seedDatabase);
            await BackupDatabaseAsync(seedDatabase, restoredDatabase);
            var restored = await CaptureDatabaseSnapshotAsync(restoredDatabase);
            AssertSnapshotsEqual(seeded, restored, "The consistent backup must restore to a distinct path exactly.");
            var expectedBusiness = restored.BusinessTables;
            var expectedSchema = restored.SchemaFingerprint;
            var expectedDefinitions = restored.DefinitionTables;

            var firstHost = await StartAndReadPausedPipelineAsync(repositoryRoot, restoredDirectory, eventStorePath,
                temporaryRoot, localApiKey, maintenanceMode: true, enableOtherAutomaticServices: true);
            ownedProcesses.Add(firstHost.Host.Process);
            AssertMaintenanceStatus(firstHost);
            await Task.Delay(TimeSpan.FromSeconds(6));
            await StopOwnedProcessAsync(firstHost.Host.Process);
            var afterFirstRestart = await CaptureDatabaseSnapshotAsync(restoredDatabase);
            Assert.AreEqual(expectedSchema, afterFirstRestart.SchemaFingerprint, "Maintenance startup must not alter schema metadata.");
            Assert.AreEqual("", FormatDifferences(expectedBusiness, afterFirstRestart.BusinessTables),
                "Maintenance startup must preserve tasks, requests, outbox, leases, holds, notifications and batch sidecars.");
            var firstDefinitionDifference = FormatDifferences(expectedDefinitions, afterFirstRestart.DefinitionTables);
            TestContext.WriteLine($"Definition catalog difference on first startup: {firstDefinitionDifference}");
            Assert.AreEqual("", firstDefinitionDifference,
                "The pre-seeded definition catalog should already match ordinary startup registration.");

            var secondHost = await StartAndReadPausedPipelineAsync(repositoryRoot, restoredDirectory, eventStorePath,
                temporaryRoot, localApiKey, maintenanceMode: true, enableOtherAutomaticServices: true);
            ownedProcesses.Add(secondHost.Host.Process);
            AssertMaintenanceStatus(secondHost);
            await Task.Delay(TimeSpan.FromSeconds(6));
            var afterSecondRestart = await CaptureDatabaseSnapshotAsync(restoredDatabase);
            Assert.AreEqual(expectedSchema, afterSecondRestart.SchemaFingerprint);
            Assert.AreEqual("", FormatDifferences(expectedBusiness, afterSecondRestart.BusinessTables),
                "A second maintenance restart must preserve the same persisted work exactly.");
            Assert.AreEqual("", FormatDifferences(expectedDefinitions, afterSecondRestart.DefinitionTables),
                "A second maintenance restart must also preserve the pre-seeded definition catalog.");

            using var anonymous = new HttpClient { BaseAddress = secondHost.Host.BaseAddress, Timeout = TimeSpan.FromSeconds(5) };
            using var unauthorized = await anonymous.PostAsJsonAsync("/api/v2/admin/collection/backfill-batches",
                new CreateBackfillBatchRequest(new(2026, 9, "JRA", "c10-manual")));
            Assert.IsTrue(unauthorized.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                "MaintenanceMode must not bypass the existing operator-authentication boundary.");

            using var authorized = new HttpClient { BaseAddress = secondHost.Host.BaseAddress, Timeout = TimeSpan.FromSeconds(10) };
            authorized.DefaultRequestHeaders.Add("X-Api-Key", localApiKey);
            using var manualResponse = await authorized.PostAsJsonAsync("/api/v2/admin/collection/backfill-batches",
                new CreateBackfillBatchRequest(new(2026, 9, "JRA", "c10-manual")));
            Assert.AreEqual(HttpStatusCode.Accepted, manualResponse.StatusCode,
                "An explicit authenticated operator backfill remains available in maintenance mode.");
            var manual = await manualResponse.Content.ReadFromJsonAsync<CreateBackfillBatchResponse>();
            Assert.AreEqual(CollectionBatchKind.Backfill, manual?.Batch.Recovery?.Kind);
            using var pipelineResponse = await authorized.GetAsync(PipelinePath);
            pipelineResponse.EnsureSuccessStatusCode();
            var pipeline = await pipelineResponse.Content.ReadFromJsonAsync<GetCollectionPipelineResponse>();
            Assert.IsTrue(pipeline?.Pipeline.IsPaused == true,
                "An explicit manual operation must not implicitly resume the incident-linked pipeline.");
            var manualRows = await ReadManualBatchRowsAsync(restoredDatabase, "c10-manual");
            Assert.AreEqual(1, manualRows.Batches);
            Assert.AreEqual(30, manualRows.Requests, "The explicit monthly backfill should retain its synchronous per-day request behavior.");
            Assert.AreEqual(30, manualRows.Tasks, "The accepted manual operation must persist per-day tasks, not only a response envelope.");
            await StopOwnedProcessAsync(secondHost.Host.Process);
        }
        finally
        {
            foreach (var process in ownedProcesses)
                await StopOwnedProcessAsync(process);
            DeleteCheckedTemporaryDirectory(temporaryRoot, "hrp-c10-maintenance-restart");
        }
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task OptionalBaselineBinary_RefusesCurrentSchemaBeforeChangingBusinessRows()
    {
        var baselineProject = Environment.GetEnvironmentVariable("HRP_BASELINE_API_PROJECT");
        if (string.IsNullOrWhiteSpace(baselineProject))
        {
            Assert.Inconclusive("Set HRP_BASELINE_API_PROJECT to an already-built older API project to run this compatibility proof.");
            return;
        }
        baselineProject = Path.GetFullPath(baselineProject);
        Assert.IsTrue(File.Exists(baselineProject), "HRP_BASELINE_API_PROJECT must point to an existing .csproj.");

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "hrp-c10-baseline-refusal", Guid.NewGuid().ToString("N"));
        var seedDirectory = Path.Combine(temporaryRoot, "seed-state");
        var targetDirectory = Path.Combine(temporaryRoot, "baseline-target");
        var seedDatabase = Path.Combine(seedDirectory, "collection-platform.db");
        var targetDatabase = Path.Combine(targetDirectory, "collection-platform.db");
        var localApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(seedDirectory);
        Directory.CreateDirectory(targetDirectory);
        Process? baselineProcess = null;

        try
        {
            await SeedPausedWorkAsync(seedDirectory);
            await BackupDatabaseAsync(seedDatabase, targetDatabase);
            var before = await CaptureDatabaseSnapshotAsync(targetDatabase);
            Assert.AreEqual(25, await ReadSchemaVersionAsync(targetDatabase),
                "The isolated baseline target must contain the current schema before launch.");

            var solutionRoot = FindSolutionRoot(Path.GetDirectoryName(baselineProject)!);
            var baseAddress = new Uri($"http://127.0.0.1:{ReserveLoopbackPort()}");
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = solutionRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
            {
                "run", "--no-build", "--no-restore", "--configuration", "Release",
                "--project", baselineProject, "--urls", baseAddress.ToString(),
            }) startInfo.ArgumentList.Add(argument);
            var env = startInfo.Environment;
            env["ASPNETCORE_ENVIRONMENT"] = "Development";
            env["DOTNET_ENVIRONMENT"] = "Development";
            env["ApiKey__Key"] = localApiKey;
            env["ConnectionStrings__EventStore"] = $"Data Source={Path.Combine(temporaryRoot, "baseline-eventstore.db")};Pooling=False";
            env["CollectionPlatform__StateDirectory"] = targetDirectory;
            env["CollectionQueue__Enabled"] = "false";
            env["CollectionOrchestration__BackgroundSchedulersEnabled"] = "false";
            env["CollectionJobWatchdog__Enabled"] = "false";
            env["CollectionDeadLetterQueueReconciler__Enabled"] = "false";
            env["JobFailureNotifications__TopicArn"] = string.Empty;
            env["DatabaseMigration__BackupBeforeMigration"] = "false";
            env["AWS_REGION"] = "ap-northeast-1";
            env["AWS_EC2_METADATA_DISABLED"] = "true";
            foreach (var name in new[]
            {
                "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN", "AWS_PROFILE",
                "AWS_DEFAULT_PROFILE", "AWS_WEB_IDENTITY_TOKEN_FILE", "AWS_SHARED_CREDENTIALS_FILE", "AWS_CONFIG_FILE",
            }) env.Remove(name);
            var keyDirectory = Path.Combine(temporaryRoot, "baseline-data-protection-keys");
            Directory.CreateDirectory(keyDirectory);
            env["DataProtection__KeysDirectory"] = keyDirectory;

            var logLines = new ConcurrentQueue<string>();
            baselineProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("The baseline API process did not start.");
            void RememberBaselineLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                logLines.Enqueue(line.Replace(localApiKey, "[redacted]", StringComparison.Ordinal));
                while (logLines.Count > 30) logLines.TryDequeue(out _);
            }
            baselineProcess.OutputDataReceived += (_, args) => RememberBaselineLine(args.Data);
            baselineProcess.ErrorDataReceived += (_, args) => RememberBaselineLine(args.Data);
            baselineProcess.BeginOutputReadLine();
            baselineProcess.BeginErrorReadLine();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await baselineProcess.WaitForExitAsync(timeout.Token);
            baselineProcess.WaitForExit();

            var after = await CaptureDatabaseSnapshotAsync(targetDatabase);
            Assert.AreNotEqual(0, baselineProcess.ExitCode,
                "An API binary that only supports the previous schema must reject the current database.");
            Assert.IsTrue(logLines.Any(line => line.Contains("schema version 25 is newer than supported version", StringComparison.OrdinalIgnoreCase)),
                "The refusal should identify schema incompatibility before normal service startup.");
            AssertSnapshotsEqual(before, after,
                "The baseline refusal must leave the copied business data and schema untouched.");
            TestContext.WriteLine("Optional baseline refusal observed; business and schema snapshots were unchanged.");
        }
        finally
        {
            if (baselineProcess is not null)
                await StopOwnedProcessAsync(baselineProcess);
            DeleteCheckedTemporaryDirectory(temporaryRoot, "hrp-c10-baseline-refusal");
        }
    }

    private static async Task SeedPausedWorkAsync(string stateDirectory)
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = stateDirectory,
            DatabaseFileName = "collection-platform.db",
        }));
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
            1, "Initial", false);
        await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
            HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
            "Race resource artifact state machine", false);
        await store.RegisterDefinitionAsync(new("race-odds"), "Race odds", CollectionResourceType.RaceOdds,
            1, "Initial", false);
        await SubjectCollectionDefinitions.RegisterAsync(store);
        var trainerDefinition = SubjectCollectionDefinitions.For(CollectionResourceType.Trainer);
        var oldTrainerRevision = trainerDefinition.CurrentRevision - 1;
        await store.RegisterDefinitionAsync(trainerDefinition.Definition, trainerDefinition.Name,
            CollectionResourceType.Trainer, oldTrainerRevision, "Older parser", trainerDefinition.PersistProfile);

        var trainer = new ResourceKey(CollectionResourceType.Trainer, "JRA", $"c9-trainer-{Guid.NewGuid():N}");
        var subjectFailure = await store.RequestAsync(trainer, trainerDefinition.Definition,
            oldTrainerRevision, CollectionReason.Discovery, now.AddSeconds(5),
            CollectionLane.Background, 50,
            attributes: new Dictionary<string, string> { ["name"] = "C9 recovery candidate" });
        var subjectLease = await store.AcquireAsync(subjectFailure.TaskId!.Value, 1, now.AddSeconds(6), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(subjectLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(subjectFailure.TaskId.Value, subjectLease.LeaseToken,
            now.AddSeconds(7), new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified",
                "C9 isolated subject-failure candidate", FailureImpact: CollectionFailureImpact.Isolated)));
        await store.RegisterDefinitionAsync(trainerDefinition.Definition, trainerDefinition.Name,
            CollectionResourceType.Trainer, trainerDefinition.CurrentRevision,
            trainerDefinition.RevisionDescription, trainerDefinition.PersistProfile);

        var raceDefinition = new CollectionDefinitionId("race-detail");
        var queuedRaceId = $"race-{Guid.NewGuid():D}";
        var queued = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", queuedRaceId),
            raceDefinition, HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
            CollectionReason.Initial, now.AddSeconds(11), CollectionLane.Normal, 50,
            effectiveDate: DateOnly.FromDateTime(now.UtcDateTime));
        var pending = (await store.GetPendingDispatchesAsync(now.AddSeconds(12), 20))
            .Single(row => row.Notification.TaskId == queued.TaskId);
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending.OutboxId],
            "c10-reservation", Guid.NewGuid(), Guid.NewGuid(), now.AddSeconds(12), TimeSpan.FromHours(1), 1));
        await store.HoldRaceForRepairAsync(queuedRaceId, Guid.NewGuid().ToString("D"), 0,
            "C10 persisted repair hold", now.AddSeconds(13));

        var runningRaceId = $"race-{Guid.NewGuid():D}";
        var running = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", runningRaceId),
            raceDefinition, HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
            CollectionReason.Initial, now.AddSeconds(14), CollectionLane.Background, 50,
            effectiveDate: DateOnly.FromDateTime(now.UtcDateTime));
        var runningLease = await store.AcquireAsync(running.TaskId!.Value, 1, now.AddSeconds(15), TimeSpan.FromHours(1));
        Assert.IsNotNull(runningLease);

        await store.CreateOrResumeBackfillBatchAsync("c10-preserved-backfill", "JRA",
            DateOnly.FromDateTime(now.UtcDateTime), DateOnly.FromDateTime(now.UtcDateTime), now.AddSeconds(16));

        var incident = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", $"race-{Guid.NewGuid():D}"),
            new("race-detail"), HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
            CollectionReason.Initial, now.AddSeconds(17), CollectionLane.Normal, 50,
            effectiveDate: DateOnly.FromDateTime(now.UtcDateTime),
            attributes: new Dictionary<string, string> { ["course"] = "NAKAYAMA", ["number"] = "3" });
        var incidentLease = await store.AcquireAsync(incident.TaskId!.Value, 1, now.AddSeconds(18), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(incidentLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(incident.TaskId.Value, incidentLease.LeaseToken,
            now.AddSeconds(19), new(CollectionAttemptResult.PermanentFailure, "C9StoppedModeIncident",
                "C9 persisted alert fixture", FailureImpact: CollectionFailureImpact.StopPipeline)));

        var paused = await store.GetPipelineStateAsync();
        Assert.IsTrue(paused.IsPaused);
        Assert.IsTrue(paused.Reason?.StartsWith("Unexpected collection failure notification ", StringComparison.Ordinal) == true,
            "Use the real incident-linked pause reason consumed by CollectionPipelineAlertDispatchService.");
    }

    private static void AssertMaintenanceStatus(
        (OwnedApiProcess Host, CollectionPipelineStateDto Pipeline, CollectionRuntimeStatusDto Runtime) host)
    {
        Assert.IsTrue(host.Pipeline.IsPaused, "MaintenanceMode must not resume the incident-linked pipeline pause.");
        Assert.HasCount(8, host.Runtime.Actions);
        foreach (var action in host.Runtime.Actions)
        {
            Assert.IsFalse(action.Enabled, $"{action.Action} must be disabled in MaintenanceMode.");
            Assert.AreEqual(CollectionRuntimeState.Disabled, action.State, action.Action.ToString());
            Assert.IsNull(action.EffectiveInterval, action.Action.ToString());
        }
    }

    private static async Task<(OwnedApiProcess Host, CollectionPipelineStateDto Pipeline, CollectionRuntimeStatusDto Runtime)> StartAndReadPausedPipelineAsync(
        string repositoryRoot, string stateDirectory, string eventStorePath, string temporaryRoot, string localApiKey,
        bool maintenanceMode = false, bool enableOtherAutomaticServices = false)
    {
        var baseAddress = new Uri($"http://127.0.0.1:{ReserveLoopbackPort()}");
        var host = StartApiProcess(repositoryRoot, stateDirectory, eventStorePath, temporaryRoot, localApiKey, baseAddress,
            maintenanceMode, enableOtherAutomaticServices);
        try
        {
            using var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(3) };
            client.DefaultRequestHeaders.Add("X-Api-Key", localApiKey);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < StartupTimeout)
            {
                if (host.Process.HasExited)
                    throw new InvalidOperationException($"The compatible API exited before its paused pipeline read was available (exit code {host.Process.ExitCode}). {host.GetLogTail()}");
                try
                {
                    using var response = await client.GetAsync(PipelinePath);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var body = await response.Content.ReadFromJsonAsync<GetCollectionPipelineResponse>();
                        using var runtimeResponse = await client.GetAsync("/api/v2/admin/collection/operations/runtime-status");
                        runtimeResponse.EnsureSuccessStatusCode();
                        var runtime = await runtimeResponse.Content.ReadFromJsonAsync<GetCollectionRuntimeStatusResponse>();
                        return (host, body?.Pipeline ?? throw new InvalidOperationException("The pipeline response was empty."),
                            runtime?.Runtime ?? throw new InvalidOperationException("The runtime response was empty."));
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(250);
            }
            throw new TimeoutException($"The compatible API did not serve the paused pipeline read in time. {host.GetLogTail()}");
        }
        catch
        {
            await StopOwnedProcessAsync(host.Process);
            throw;
        }
    }

    private static OwnedApiProcess StartApiProcess(string repositoryRoot, string stateDirectory, string eventStorePath,
        string temporaryRoot, string localApiKey, Uri baseAddress, bool maintenanceMode, bool enableOtherAutomaticServices)
    {
        var apiProject = Path.Combine(repositoryRoot, "src", "HorseRacingPrediction.Api", "HorseRacingPrediction.Api.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "run", "--no-build", "--no-restore", "--no-launch-profile", "--configuration", "Release",
            "--project", apiProject, "--urls", baseAddress.ToString(),
        }) startInfo.ArgumentList.Add(argument);

        var environment = startInfo.Environment;
        environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        environment["DOTNET_ENVIRONMENT"] = "Development";
        environment["ApiKey__Key"] = localApiKey;
        environment["ConnectionStrings__EventStore"] = $"Data Source={eventStorePath};Pooling=False";
        environment["CollectionPlatform__StateDirectory"] = stateDirectory;
        if (maintenanceMode) environment["CollectionPlatform__MaintenanceMode"] = "true";
        else environment.Remove("CollectionPlatform__MaintenanceMode");
        environment["CollectionQueue__Enabled"] = enableOtherAutomaticServices ? "true" : "false";
        environment["CollectionQueue__Provider"] = enableOtherAutomaticServices ? "Local" : "Sqs";
        environment["CollectionQueue__LocalDatabasePath"] = Path.Combine(stateDirectory, "local-queue.db");
        environment["CollectionOrchestration__BackgroundSchedulersEnabled"] = enableOtherAutomaticServices ? "true" : "false";
        environment["CollectionJobWatchdog__Enabled"] = enableOtherAutomaticServices ? "true" : "false";
        environment["CollectionDeadLetterQueueReconciler__Enabled"] = enableOtherAutomaticServices ? "true" : "false";
        environment["JobFailureNotifications__Enabled"] = enableOtherAutomaticServices ? "true" : "false";
        environment["JobFailureNotifications__TopicArn"] = string.Empty;
        environment["DatabaseMigration__BackupBeforeMigration"] = "false";
        environment["AWS_REGION"] = "ap-northeast-1";
        environment["AWS_EC2_METADATA_DISABLED"] = "true";
        foreach (var name in new[]
        {
            "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN", "AWS_PROFILE",
            "AWS_DEFAULT_PROFILE", "AWS_WEB_IDENTITY_TOKEN_FILE", "AWS_SHARED_CREDENTIALS_FILE", "AWS_CONFIG_FILE",
        }) environment.Remove(name);

        var dataProtectionDirectory = Path.Combine(temporaryRoot, "data-protection-keys");
        Directory.CreateDirectory(dataProtectionDirectory);
        environment["DataProtection__KeysDirectory"] = dataProtectionDirectory;
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The local API process did not start.");
        var logs = new ConcurrentQueue<string>();
        void Remember(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            logs.Enqueue(line.Replace(localApiKey, "[redacted]", StringComparison.Ordinal));
            while (logs.Count > 40) logs.TryDequeue(out _);
        }
        process.OutputDataReceived += (_, args) => Remember(args.Data);
        process.ErrorDataReceived += (_, args) => Remember(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new(process, baseAddress, () => string.Join(Environment.NewLine, logs.ToArray().TakeLast(20)));
    }

    private static async Task<bool> WaitForTableMutationAsync(string databasePath, DatabaseSnapshot baseline,
        string tableName, TimeSpan timeout)
    {
        var expected = baseline.BusinessTables.GetValueOrDefault(tableName);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var current = await CaptureDatabaseSnapshotAsync(databasePath);
            if (current.BusinessTables.GetValueOrDefault(tableName) != expected) return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static async Task<DatabaseSnapshot> CaptureDatabaseSnapshotAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        var tableNames = new List<string>();
        await using (var tables = connection.CreateCommand())
        {
            tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'collection_%' ORDER BY name;";
            await using var reader = await tables.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tableNames.Add(reader.GetString(0));
        }

        var business = new Dictionary<string, TableSnapshot>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, TableSnapshot>(StringComparer.Ordinal);
        foreach (var tableName in tableNames)
        {
            var rows = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var values = new string?[reader.FieldCount];
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    var value = reader.GetValue(index);
                    values[index] = value is DBNull ? null : value switch
                    {
                        byte[] bytes => Convert.ToBase64String(bytes),
                        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
                    };
                }
                rows.Add(JsonSerializer.Serialize(values));
            }
            rows.Sort(StringComparer.Ordinal);
            var canonicalRows = string.Join("\n", rows);
            var snapshot = new TableSnapshot(rows.Count,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRows))));
            if (tableName is "collection_definitions" or "collection_revisions") definitions.Add(tableName, snapshot);
            else business.Add(tableName, snapshot);
        }

        await using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText = "SELECT type, name, tbl_name, COALESCE(sql, '') FROM sqlite_master "
            + "WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name;";
        await using var schemaReader = await schemaCommand.ExecuteReaderAsync();
        var schemaRows = new List<string>();
        while (await schemaReader.ReadAsync())
            schemaRows.Add(string.Join("\u001f", Enumerable.Range(0, schemaReader.FieldCount)
                .Select(index => schemaReader.GetString(index))));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", schemaRows))));
        return new(business, definitions, fingerprint);
    }

    private static async Task<string> ReadTargetEvidenceAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        var subjectRows = new List<string>();
        await using (var subject = connection.CreateCommand())
        {
            subject.CommandText = "SELECT task.RequestedRevision, task.Status, request.Reason, "
                + "CASE WHEN request.BatchId LIKE 'subject-auto-recovery:%' THEN 1 ELSE 0 END "
                + "FROM collection_tasks AS task "
                + "JOIN collection_resources AS resource ON resource.ResourcePk = task.ResourcePk "
                + "JOIN collection_requests AS request ON request.RequestId = task.RequestId "
                + "WHERE resource.ResourceId LIKE 'c9-trainer-%' ORDER BY task.CreatedAt, task.TaskId;";
            await using var reader = await subject.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                subjectRows.Add($"revision={reader.GetValue(0)},status={reader.GetValue(1)},reason={reader.GetValue(2)},autoRecoveryBatch={reader.GetInt64(3) == 1}");
        }

        var alertRows = new List<string>();
        await using (var alert = connection.CreateCommand())
        {
            alert.CommandText = "SELECT notification.PublishAttemptCount, "
                + "CASE WHEN notification.LastPublishError IS NULL THEN 0 ELSE 1 END, "
                + "CASE WHEN notification.PublishedAt IS NULL THEN 0 ELSE 1 END, notification.ResolutionStatus "
                + "FROM collection_failure_notifications AS notification "
                + "JOIN collection_tasks AS task ON task.TaskId = notification.TaskId "
                + "JOIN collection_resources AS resource ON resource.ResourcePk = task.ResourcePk "
                + "WHERE notification.ErrorCode = 'C9StoppedModeIncident' ORDER BY notification.FailedAt;";
            await using var reader = await alert.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                alertRows.Add($"publishAttempts={reader.GetValue(0)},lastErrorPresent={reader.GetInt64(1) == 1},published={reader.GetInt64(2) == 1},resolution={reader.GetValue(3)}");
        }
        return $"subject=[{string.Join(";", subjectRows)}], alert=[{string.Join(";", alertRows)}]";
    }

    private static async Task BackupDatabaseAsync(string sourcePath, string destinationPath)
    {
        await using var source = new SqliteConnection($"Data Source={sourcePath};Pooling=False");
        await source.OpenAsync();
        await using var destination = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        await destination.OpenAsync();
        source.BackupDatabase(destination);
    }

    private static void AssertSnapshotsEqual(DatabaseSnapshot expected, DatabaseSnapshot actual, string message)
    {
        var businessDifferences = FormatDifferences(expected.BusinessTables, actual.BusinessTables);
        var definitionDifferences = FormatDifferences(expected.DefinitionTables, actual.DefinitionTables);
        Assert.AreEqual(expected.SchemaFingerprint, actual.SchemaFingerprint, message + " Schema objects changed.");
        Assert.AreEqual("", businessDifferences, message + " Business table differences: " + businessDifferences);
        Assert.AreEqual("", definitionDifferences, message + " Definition/revision differences: " + definitionDifferences);
    }

    private static string FormatDifferences(IReadOnlyDictionary<string, TableSnapshot> expected,
        IReadOnlyDictionary<string, TableSnapshot> actual)
    {
        var differences = expected.Keys.Union(actual.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
            .Where(name => !expected.TryGetValue(name, out var before)
                || !actual.TryGetValue(name, out var after) || before != after)
            .Select(name =>
            {
                var before = expected.TryGetValue(name, out var value) ? value.RowCount : 0;
                var after = actual.TryGetValue(name, out value) ? value.RowCount : 0;
                return $"{name}({before}->{after})";
            });
        return string.Join(", ", differences);
    }

    private static bool HasAnyDifference(IReadOnlyDictionary<string, TableSnapshot> before,
        IReadOnlyDictionary<string, TableSnapshot> after, params string[] tableNames)
        => tableNames.Any(name => !before.TryGetValue(name, out var left)
            || !after.TryGetValue(name, out var right) || left != right);

    private static async Task StopOwnedProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
        }
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository solution from the test output directory.");
    }

    private static string FindSolutionRoot(string startDirectory)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the baseline solution from HRP_BASELINE_API_PROJECT.");
    }

    private static async Task<int> ReadSchemaVersionAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM collection_schema_history;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<(int Batches, int Requests, int Tasks)> ReadManualBatchRowsAsync(
        string databasePath, string batchId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        async Task<int> CountAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$batchId", batchId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        var batches = await CountAsync("SELECT COUNT(*) FROM collection_backfill_batches WHERE BatchId = $batchId;");
        var requests = await CountAsync("SELECT COUNT(*) FROM collection_requests WHERE BatchId = $batchId;");
        var tasks = await CountAsync("SELECT COUNT(*) FROM collection_tasks AS task "
            + "JOIN collection_requests AS request ON request.RequestId = task.RequestId WHERE request.BatchId = $batchId;");
        return (batches, requests, tasks);
    }

    private static void DeleteCheckedTemporaryDirectory(string temporaryRoot, string expectedDirectoryName)
    {
        var fullPath = Path.GetFullPath(temporaryRoot);
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), expectedDirectoryName))
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(fullPath)) return;
        Directory.Delete(fullPath, recursive: true);
    }

    private sealed record TableSnapshot(int RowCount, string Digest);
    private sealed record DatabaseSnapshot(Dictionary<string, TableSnapshot> BusinessTables,
        Dictionary<string, TableSnapshot> DefinitionTables, string SchemaFingerprint);
    private sealed record OwnedApiProcess(Process Process, Uri BaseAddress, Func<string> GetLogTail);
}
