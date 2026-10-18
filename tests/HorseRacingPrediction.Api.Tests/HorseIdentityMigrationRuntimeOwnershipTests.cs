using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationRuntimeOwnershipTests
{
    private const string MaintenanceChildSourcesVariable = "HRP_HORSE_MAINTENANCE_CHILD_SOURCES";
    private const string MaintenanceChildJournalVariable = "HRP_HORSE_MAINTENANCE_CHILD_JOURNAL";
    private const string MaintenanceChildOperationVariable = "HRP_HORSE_MAINTENANCE_CHILD_OPERATION";
    private const string MaintenanceChildHashVariable = "HRP_HORSE_MAINTENANCE_CHILD_HASH";

    [TestMethod]
    public void ConfiguredSourcesPreserveEachRuntimeBaseWithoutCreatingFilesystemEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-source-resolution-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(root, "content-root");
        var currentDirectory = Path.Combine(root, "working-directory");
        var applicationBase = Path.Combine(root, "application-base");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:EventStore"] = "Data Source=event-data/eventstore.db",
        }).Build();
        var collection = new CollectionPlatformOptions
        {
            StateDirectory = "collection-data",
            DatabaseFileName = "collection.db",
        };
        var schedule = new PredictionScheduleOptions();
        var queue = new CollectionQueueOptions
        {
            Provider = "Local",
            LocalDatabasePath = "queue-data/local-queue.db",
        };

        var paths = HorseIdentityMigrationConfiguredSources.Resolve(configuration, collection, schedule, queue,
            contentRoot, currentDirectory, applicationBase);

        Assert.AreEqual(Path.GetFullPath(Path.Combine(contentRoot, "event-data", "eventstore.db")), paths.EventStorePath);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(contentRoot, "collection-data", "collection.db")),
            paths.CollectionPlatformPath);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(applicationBase, "prediction-scheduling-state",
            "prediction-executions.db")), paths.PredictionSchedulePath);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(currentDirectory, "queue-data", "local-queue.db")),
            paths.LocalCollectionQueuePath);
        Assert.IsFalse(Directory.Exists(root), "Resolving configured sources must not create directories or files.");
    }

    [TestMethod]
    public void ConfiguredSourcesUseWorkingDirectoryForExplicitRelativeScheduleAndQueueDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-source-relative-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(root, "content-root");
        var currentDirectory = Path.Combine(root, "working-directory");
        var applicationBase = Path.Combine(root, "application-base");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:EventStore"] = "Data Source=:memory:",
        }).Build();
        var paths = HorseIdentityMigrationConfiguredSources.Resolve(configuration,
            new CollectionPlatformOptions(),
            new PredictionScheduleOptions { StateDirectory = "custom-schedule", StoreFileName = "custom.db" },
            new CollectionQueueOptions { Provider = "Local", LocalDatabasePath = "" },
            contentRoot, currentDirectory, applicationBase);

        Assert.IsNull(paths.EventStorePath, "An in-memory EventStore has no physical source file to reserve.");
        Assert.AreEqual(Path.GetFullPath(Path.Combine(contentRoot, "collection-platform-state", "collection-platform.db")),
            paths.CollectionPlatformPath);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(currentDirectory, "custom-schedule", "custom.db")),
            paths.PredictionSchedulePath);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(currentDirectory,
            "collection-platform-state", "local-collection-queue.db")), paths.LocalCollectionQueuePath);
        Assert.IsFalse(Directory.Exists(root), "Path resolution must remain filesystem-free.");

        var sqs = HorseIdentityMigrationConfiguredSources.Resolve(configuration, new CollectionPlatformOptions(),
            new PredictionScheduleOptions(), new CollectionQueueOptions { Provider = "Sqs" },
            contentRoot, currentDirectory, applicationBase);
        Assert.IsNull(sqs.LocalCollectionQueuePath, "SQS configuration has no local queue file to reserve.");
        Assert.IsFalse(Directory.Exists(root), "Resolving SQS configuration must not create local queue state.");
    }

    [TestMethod]
    public void ConfiguredSourcesFailClosedForInvalidMandatoryPaths()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:EventStore"] = "Data Source=:memory:",
        }).Build();
        var queue = new CollectionQueueOptions { Provider = "Local", LocalDatabasePath = "queue.db" };

        Assert.ThrowsExactly<ArgumentException>(() => HorseIdentityMigrationConfiguredSources.Resolve(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EventStore"] = "Data Source=invalid\0source",
            }).Build(), new CollectionPlatformOptions(), new PredictionScheduleOptions(), queue,
            Path.GetTempPath(), Environment.CurrentDirectory, AppContext.BaseDirectory));
        Assert.ThrowsExactly<ArgumentException>(() => HorseIdentityMigrationConfiguredSources.Resolve(configuration,
            new CollectionPlatformOptions { StateDirectory = "\0" }, new PredictionScheduleOptions(), queue,
            Path.GetTempPath(), Environment.CurrentDirectory, AppContext.BaseDirectory));
        Assert.ThrowsExactly<ArgumentException>(() => HorseIdentityMigrationConfiguredSources.Resolve(configuration,
            new CollectionPlatformOptions(), new PredictionScheduleOptions { StateDirectory = "\0" }, queue,
            Path.GetTempPath(), Environment.CurrentDirectory, AppContext.BaseDirectory));
        Assert.ThrowsExactly<ArgumentException>(() => HorseIdentityMigrationConfiguredSources.Resolve(configuration,
            new CollectionPlatformOptions(), new PredictionScheduleOptions(),
            new CollectionQueueOptions { Provider = "Local", LocalDatabasePath = "\0" },
            Path.GetTempPath(), Environment.CurrentDirectory, AppContext.BaseDirectory));
    }

    [TestMethod]
    public void RuntimeOwnershipReservesAbsentSourceParentsAndSerializesOnlyOverlappingSets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-source-ownership-{Guid.NewGuid():N}");
        var firstPath = Path.Combine(root, "first", "store.db");
        var secondPath = Path.Combine(root, "second", "store.db");

        var first = HorseIdentityMigrationRuntimeOwnership.Acquire([firstPath]);
        try
        {
            Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(firstPath)));
            Assert.IsFalse(File.Exists(firstPath), "Acquiring ownership reserves an absent DB without creating it.");
            Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire([firstPath]));
            using (HorseIdentityMigrationRuntimeOwnership.Acquire([secondPath]))
                Assert.IsFalse(File.Exists(secondPath));
        }
        finally
        {
            first.Dispose();
        }

        using var reacquired = HorseIdentityMigrationRuntimeOwnership.Acquire([firstPath]);
    }

    [TestMethod]
    public void RuntimeOwnershipRejectsCorruptGateAndRollsBackEarlierAcquisitions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-source-rollback-{Guid.NewGuid():N}");
        var firstPath = Path.Combine(root, "a", "store.db");
        var secondPath = Path.Combine(root, "b", "store.db");
        using (HorseIdentityMigrationRuntimeOwnership.Acquire([secondPath])) { }
        var secondGate = Path.Combine(Path.GetDirectoryName(secondPath)!, ".store.db.horse-identity-migration.lock");
        File.WriteAllText(secondGate, "corrupt");
        var corruptBytes = File.ReadAllBytes(secondGate);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            HorseIdentityMigrationRuntimeOwnership.Acquire([firstPath, secondPath]));
        CollectionAssert.AreEqual(corruptBytes, File.ReadAllBytes(secondGate),
            "Opening a gate that appeared during CreateNew must validate it without replacing corrupt evidence.");
        using var firstCanBeAcquired = HorseIdentityMigrationRuntimeOwnership.Acquire([firstPath]);
        Assert.IsFalse(File.Exists(firstPath));
    }

    [TestMethod]
    public async Task MaintenanceAuthorizationUsesLiveControlLeaseAndRealApiThenCliChildren()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-maintenance-authority-{Guid.NewGuid():N}");
        var firstPath = Path.Combine(root, "a", "store.db");
        var secondPath = Path.Combine(root, "b", "store.db");
        var paths = new[] { firstPath, secondPath };
        Directory.CreateDirectory(root);
        try
        {
            using var control = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(paths);
            using (HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease([Path.Combine(root, "disjoint", "store.db")])) { }
            Assert.ThrowsExactly<IOException>(() =>
                HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease([secondPath]));

            var bootstrap = control.IssueApiBootstrapAuthorization();
            Assert.IsTrue(await RunMaintenanceChildAsync("bootstrap", paths,
                bootstrap.SerializeForPrivateEnvironment()));
            Assert.IsTrue(await RunMaintenanceChildAsync("relay-bootstrap", paths,
                bootstrap.SerializeForPrivateEnvironment()),
                "A live but unrelated intermediary process cannot replay the child authorization.");
            Assert.IsFalse(HorseIdentityMigrationMaintenanceCapability.OperationAuthorization.TryImportAndValidate(
                bootstrap.SerializeForPrivateEnvironment(), paths, out _),
                "An operation-less bootstrap proof cannot be imported as CLI authority.");

            var tampered = TamperLastBase64Field(bootstrap.SerializeForPrivateEnvironment());
            Assert.IsFalse(HorseIdentityMigrationMaintenanceCapability.ApiBootstrapAuthorization.TryImportAndValidate(
                tampered, paths, out _));
            Assert.IsFalse(HorseIdentityMigrationMaintenanceCapability.ApiBootstrapAuthorization.TryImportAndValidate(
                bootstrap.SerializeForPrivateEnvironment(), [firstPath], out _),
                "A proof must bind the complete canonical source set.");

            var journalDirectory = Path.Combine(root, "journal");
            var (journal, operationId, requestHash) = await CreateStoppingApiJournal(journalDirectory, paths);
            Assert.IsTrue(await RunMaintenanceChildAsync("install-api", paths,
                bootstrap.SerializeForPrivateEnvironment(), journalDirectory, operationId, requestHash));

            Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire(paths),
                "An ordinary API cannot acquire a source while a durable maintenance gate exists.");

            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
                HorseIdentityMigrationExecutionPhase.ApiExited, DateTimeOffset.UtcNow)).Succeeded);
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
                HorseIdentityMigrationExecutionPhase.BackedUp, DateTimeOffset.UtcNow)).Succeeded);
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
                HorseIdentityMigrationExecutionPhase.Applying, DateTimeOffset.UtcNow)).Succeeded);
            var operationAuthorization = await control.IssueOperationAuthorizationAsync(operationId, requestHash, journal);
            Assert.IsTrue(await RunMaintenanceChildAsync("install-cli", paths,
                operationAuthorization.SerializeForPrivateEnvironment(), journalDirectory, operationId, requestHash));

            await CompleteAppliedJournalAsync(journal, operationId);
            using (var gateManagementOwnership = HorseIdentityMigrationRuntimeOwnership.AcquireForMaintenanceGateManagement(
                       paths, control, operationAuthorization))
                await HorseIdentityMigrationMaintenanceOwnership.ClearMaintenanceGatesAsync(
                    gateManagementOwnership, control, operationAuthorization, journal);
            Assert.IsTrue(paths.All(path => !File.Exists(
                HorseIdentityMigrationMaintenanceOwnership.GetMaintenanceGatePath(path))));
            using (HorseIdentityMigrationRuntimeOwnership.Acquire(paths)) { }

            control.Dispose();
            Assert.IsFalse(HorseIdentityMigrationMaintenanceCapability.OperationAuthorization.TryImportAndValidate(
                operationAuthorization.SerializeForPrivateEnvironment(), paths, out _),
                "A copied operation proof is invalid after its parent control lease is released.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task MaintenanceGateInstallRollsBackOnlyItsNewMarkersOnCorruptSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-maintenance-partial-{Guid.NewGuid():N}");
        var firstPath = Path.Combine(root, "a", "store.db");
        var secondPath = Path.Combine(root, "b", "store.db");
        var paths = new[] { firstPath, secondPath };
        Directory.CreateDirectory(root);
        try
        {
            using var control = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(paths);
            var bootstrap = control.IssueApiBootstrapAuthorization();
            using var runtimeOwnership = HorseIdentityMigrationRuntimeOwnership.Acquire(paths);
            var journalDirectory = Path.Combine(root, "journal");
            var (journal, operationId, requestHash) = await CreateStoppingApiJournal(journalDirectory, paths);
            var corruptGate = HorseIdentityMigrationMaintenanceOwnership.GetMaintenanceGatePath(secondPath);
            File.WriteAllText(corruptGate, "corrupt-maintenance-marker");
            var corruptBytes = File.ReadAllBytes(corruptGate);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                HorseIdentityMigrationMaintenanceOwnership.InstallMaintenanceGatesAsync(runtimeOwnership,
                    bootstrap, operationId, requestHash, journal));
            Assert.IsFalse(File.Exists(HorseIdentityMigrationMaintenanceOwnership.GetMaintenanceGatePath(firstPath)),
                "The first gate created by this failed attempt is rolled back.");
            CollectionAssert.AreEqual(corruptBytes, File.ReadAllBytes(corruptGate),
                "A pre-existing corrupt marker is never replaced or deleted.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task StandaloneOwnerCanInstallAndClearOnlyAfterSucceededVerification()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-maintenance-standalone-{Guid.NewGuid():N}");
        var paths = new[] { Path.Combine(root, "a", "store.db"), Path.Combine(root, "b", "store.db") };
        Directory.CreateDirectory(root);
        try
        {
            using var control = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(paths);
            using var runtime = HorseIdentityMigrationRuntimeOwnership.Acquire(paths);
            var (journal, operationId, requestHash) = await CreatePreparingApiJournal(
                Path.Combine(root, "journal"), paths);
            await HorseIdentityMigrationMaintenanceOwnership.InstallStandaloneMaintenanceGatesAsync(
                runtime, control, operationId, requestHash, journal);
            Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire(paths));

            foreach (var phase in new[]
            {
                HorseIdentityMigrationExecutionPhase.Draining,
                HorseIdentityMigrationExecutionPhase.StoppingApi,
                HorseIdentityMigrationExecutionPhase.ApiExited,
                HorseIdentityMigrationExecutionPhase.BackedUp,
                HorseIdentityMigrationExecutionPhase.Applying,
            })
                Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase,
                    DateTimeOffset.UtcNow)).Succeeded);
            var operationAuthorization = await control.IssueOperationAuthorizationAsync(operationId, requestHash, journal);
            await CompleteAppliedJournalAsync(journal, operationId);
            runtime.Dispose();
            using var gateManagement = HorseIdentityMigrationRuntimeOwnership.AcquireForMaintenanceGateManagement(
                paths, control, operationAuthorization);
            await HorseIdentityMigrationMaintenanceOwnership.ClearMaintenanceGatesAsync(gateManagement, control,
                operationAuthorization, journal);
            Assert.IsTrue(paths.All(path => !File.Exists(
                HorseIdentityMigrationMaintenanceOwnership.GetMaintenanceGatePath(path))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ReadonlyReverificationAdoptsOldGatesWithoutProducingOperationAuthority()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-maintenance-reverify-{Guid.NewGuid():N}");
        var paths = new[] { Path.Combine(root, "a", "store.db"), Path.Combine(root, "b", "store.db") };
        Directory.CreateDirectory(root);
        try
        {
            HorseIdentityMigrationMaintenanceCapability.ReadonlyReverificationAuthorization readonlyAuthorization;
            HorseIdentityMigrationJournal journal;
            Guid operationId;
            string requestHash;
            using (var previousControl = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(paths))
            {
                var bootstrap = previousControl.IssueApiBootstrapAuthorization();
                (journal, operationId, requestHash) = await CreateStoppingApiJournal(Path.Combine(root, "journal"), paths);
                Assert.IsTrue(await RunMaintenanceChildAsync("install-api", paths,
                    bootstrap.SerializeForPrivateEnvironment(), Path.Combine(root, "journal"), operationId, requestHash));
                foreach (var phase in new[]
                {
                    HorseIdentityMigrationExecutionPhase.ApiExited,
                    HorseIdentityMigrationExecutionPhase.BackedUp,
                    HorseIdentityMigrationExecutionPhase.Applying,
                })
                    Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase,
                        DateTimeOffset.UtcNow)).Succeeded);
                _ = await previousControl.IssueOperationAuthorizationAsync(operationId, requestHash, journal);
                await CompleteAppliedJournalAsync(journal, operationId, leaveVerifying: true);
            }

            using var currentControl = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(paths);
            readonlyAuthorization = (await currentControl.IssueReadonlyReverificationAuthorizationAsync(
                operationId, requestHash, journal))!;
            Assert.IsNotNull(readonlyAuthorization,
                "A succeeded operation with gates from the previous supervisor can be adopted for explicit readonly re-verification.");
            using var readonlyOwnership = await HorseIdentityMigrationRuntimeOwnership.AcquireForReadonlyReverificationAsync(
                paths, currentControl, readonlyAuthorization, journal);
            Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire(paths));
            Assert.IsFalse(HorseIdentityMigrationMaintenanceCapability.OperationAuthorization.TryImportAndValidate(
                readonlyAuthorization.SerializeForPrivateEnvironment(), paths, out _),
                "Readonly re-verification authority is never CLI/apply authority.");
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
                HorseIdentityMigrationExecutionPhase.Verified, DateTimeOffset.UtcNow)).Succeeded);
            await HorseIdentityMigrationMaintenanceOwnership.ClearMaintenanceGatesAfterReadonlyReverificationAsync(
                readonlyOwnership, currentControl, readonlyAuthorization, journal);
            Assert.IsTrue(paths.All(path => !File.Exists(
                HorseIdentityMigrationMaintenanceOwnership.GetMaintenanceGatePath(path))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void RuntimeOwnershipRejectsUnixDomainSocketAsNonRegularSource()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.Combine(Path.GetTempPath(), $"horse-source-socket-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var socketPath = Path.Combine(root, "source.db");
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(socketPath));
        try
        {
            Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire([socketPath]),
                "A Unix socket must not satisfy the regular-file type check.");
        }
        finally
        {
            socket.Dispose();
            File.Delete(socketPath);
        }
    }

    [TestMethod]
    public void RuntimeOwnershipCanonicalizesWindowsCaseAndShortPathAliases()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), $"horse-source-windows-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var existingPath = Path.Combine(root, "LongSourceFileName.db");
        File.WriteAllText(existingPath, "source");
        var existingCaseAlias = Path.Combine(root, "longsourcefilename.DB");
        using (var existingOwner = HorseIdentityMigrationRuntimeOwnership.Acquire([existingPath]))
            Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire([existingCaseAlias]));
        using (HorseIdentityMigrationRuntimeOwnership.Acquire([existingCaseAlias])) { }

        var absentPath = Path.Combine(root, "AbsentSourceFileName.db");
        var absentCaseAlias = Path.Combine(root, "absentsourcefilename.DB");
        using (var absentOwner = HorseIdentityMigrationRuntimeOwnership.Acquire([absentPath]))
            Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire([absentCaseAlias]));
        using (HorseIdentityMigrationRuntimeOwnership.Acquire([absentCaseAlias]))
            Assert.IsFalse(File.Exists(absentPath));

        var shortPath = new StringBuilder(1024);
        var shortPathLength = GetShortPathNameWindows(existingPath, shortPath, (uint)shortPath.Capacity);
        if (shortPathLength == 0 || shortPathLength >= shortPath.Capacity
            || HorseIdentityMigrationConfiguredSources.PathComparer.Equals(existingPath, shortPath.ToString()))
            Assert.Inconclusive("This Windows volume does not expose a distinct 8.3 short-path alias.");

        var shortAlias = shortPath.ToString();
        using (var longNameOwner = HorseIdentityMigrationRuntimeOwnership.Acquire([existingPath]))
            Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire([shortAlias]));
        using (HorseIdentityMigrationRuntimeOwnership.Acquire([shortAlias])) { }
    }

    [TestMethod]
    public void RuntimeOwnershipRejectsSymbolicLinksHardLinksAndNonRegularSourcePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-source-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var targetDirectory = Path.Combine(root, "target-directory");
        Directory.CreateDirectory(targetDirectory);
        var target = Path.Combine(targetDirectory, "target.db");
        File.WriteAllText(target, "source");

        var symbolicDirectory = Path.Combine(root, "symbolic-directory");
        CreateDirectoryLink(symbolicDirectory, targetDirectory);
        Assert.IsTrue(Directory.Exists(symbolicDirectory), "The reparse-point counterexample must resolve to its target.");
        Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire(
            [Path.Combine(symbolicDirectory, "target.db")]));
        var redirectedMissingParent = Path.Combine(targetDirectory, "created-if-followed");
        Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire(
            [Path.Combine(symbolicDirectory, "created-if-followed", "new.db")]));
        Assert.IsFalse(Directory.Exists(redirectedMissingParent),
            "Validation must reject a reparse ancestor before creating missing directories under its target.");

        var hardLink = Path.Combine(root, "hard.db");
        CreateHardLink(hardLink, target);
        Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire([hardLink]));
        Assert.ThrowsExactly<IOException>(() => HorseIdentityMigrationRuntimeOwnership.Acquire([root]));
    }

    private static void CreateHardLink(string linkPath, string targetPath)
    {
        var succeeded = OperatingSystem.IsWindows()
            ? CreateHardLinkWindows(linkPath, targetPath, IntPtr.Zero)
            : CreateHardLinkUnix(targetPath, linkPath) == 0;
        if (!succeeded) throw new IOException("The test could not create a hard-link counterexample.");
    }

    private static async Task<(HorseIdentityMigrationJournal Journal, Guid OperationId, string RequestHash)>
        CreateStoppingApiJournal(string journalDirectory, IReadOnlyList<string> sourcePaths)
    {
        var journal = new HorseIdentityMigrationJournal(journalDirectory, sourcePaths);
        var operationId = Guid.NewGuid();
        var fingerprint = new string('A', 64);
        var manifest = new HorseIdentityMigrationManifest(
        [
            new HorseIdentityMigrationManifestEntry(
                "horse-00000000-0000-0000-0000-000000000001", "jra-horse:2024100801", true),
        ]);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        Assert.IsTrue((await journal.StageWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
            fingerprint, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash,
            DateTimeOffset.UtcNow)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
        })
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase,
                DateTimeOffset.UtcNow)).Succeeded);
        return (journal, operationId, requestHash);
    }

    private static async Task CompleteAppliedJournalAsync(HorseIdentityMigrationJournal journal, Guid operationId,
        bool leaveVerifying = false)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, now)).Succeeded);
        var lease = await journal.TryAcquireExecutionAsync(operationId, now);
        Assert.IsNotNull(lease);
        await using (lease!)
        {
            var evidence = new TypedHorseIdentityPageEvidence(MigrationStoreSurface.EventStore, "EventEntity", 1,
                Hash("before-page"), Hash("after-page"), "0", "0");
            var membershipHash = Hash("membership-page");
            var prepared = new HorseIdentityMigrationPageCheckpoint(evidence, membershipHash,
                HorseIdentityMigrationPageState.Prepared);
            Assert.IsTrue((await journal.RegisterRequiredPagesAsync(operationId, [prepared], now)).Succeeded);
            foreach (var state in new[]
            {
                HorseIdentityMigrationPageState.Prepared,
                HorseIdentityMigrationPageState.CommittedUnverified,
                HorseIdentityMigrationPageState.Verified,
            })
                Assert.IsTrue((await journal.RecordPageCheckpointAsync(operationId,
                    prepared with { State = state }, now)).Succeeded);
            await lease!.CompleteAsync(now);
        }

        var phases = leaveVerifying
            ? new[] { HorseIdentityMigrationExecutionPhase.DataSucceeded, HorseIdentityMigrationExecutionPhase.Verifying }
            : new[] { HorseIdentityMigrationExecutionPhase.DataSucceeded, HorseIdentityMigrationExecutionPhase.Verifying,
                HorseIdentityMigrationExecutionPhase.Verified };
        foreach (var phase in phases)
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, now)).Succeeded);
    }

    private static async Task<(HorseIdentityMigrationJournal Journal, Guid OperationId, string RequestHash)>
        CreatePreparingApiJournal(string journalDirectory, IReadOnlyList<string> sourcePaths)
    {
        var journal = new HorseIdentityMigrationJournal(journalDirectory, sourcePaths);
        var operationId = Guid.NewGuid();
        var fingerprint = new string('B', 64);
        var manifest = new HorseIdentityMigrationManifest(
        [
            new HorseIdentityMigrationManifestEntry(
                "horse-00000000-0000-0000-0000-000000000001", "jra-horse:2024100801", true),
        ]);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        Assert.IsTrue((await journal.StageWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
            fingerprint, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash,
            DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Preparing, DateTimeOffset.UtcNow)).Succeeded);
        return (journal, operationId, requestHash);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static readonly SemaphoreSlim MaintenanceChildBuildGate = new(1, 1);
    private static string? _maintenanceChildAssemblyPath;

    private static async Task<bool> RunMaintenanceChildAsync(string mode, IReadOnlyList<string> sourcePaths,
        string authorization, string? journalDirectory = null, Guid? operationId = null, string? requestHash = null)
    {
        var helperAssemblyPath = await GetMaintenanceChildAssemblyPathAsync();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(helperAssemblyPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(helperAssemblyPath);
        startInfo.ArgumentList.Add(mode);
        startInfo.Environment["HRP_MAINTENANCE_HELPER_DLL"] = helperAssemblyPath;
        startInfo.Environment["HRP_MAINTENANCE_API_DLL"] = Path.Combine(AppContext.BaseDirectory,
            "HorseRacingPrediction.Api.dll");
        startInfo.Environment[MaintenanceChildSourcesVariable] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(string.Join('\n', sourcePaths)));
        startInfo.Environment[HorseIdentityMigrationMaintenanceCapability.PrivateEnvironmentVariable] = authorization;
        if (journalDirectory is not null) startInfo.Environment[MaintenanceChildJournalVariable] = journalDirectory;
        if (operationId is not null) startInfo.Environment[MaintenanceChildOperationVariable] = operationId.Value.ToString("D");
        if (requestHash is not null) startInfo.Environment[MaintenanceChildHashVariable] = requestHash;

        using var process = Process.Start(startInfo)
            ?? throw new IOException("Could not start the direct authorization validation child.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The direct authorization validation child exceeded its two-minute bound.");
        }

        var output = await outputTask;
        var error = await errorTask;
        var expectedOutput = mode == "relay-bootstrap" ? "rejected-by-actual-parent-check" : $"{mode}-validated";
        if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error)
            || !string.Equals(output.Trim(), expectedOutput, StringComparison.Ordinal))
        {
            Console.WriteLine($"Maintenance child '{mode}' failed; exit={process.ExitCode}; output={output}; error={error}");
            return false;
        }
        return true;
    }

    private static async Task<string> GetMaintenanceChildAssemblyPathAsync()
    {
        if (_maintenanceChildAssemblyPath is not null) return _maintenanceChildAssemblyPath;
        await MaintenanceChildBuildGate.WaitAsync();
        try
        {
            if (_maintenanceChildAssemblyPath is not null) return _maintenanceChildAssemblyPath;
            var directory = Path.Combine(Path.GetTempPath(), $"hrp-maintenance-child-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var projectPath = Path.Combine(directory, "MaintenanceChild.csproj");
            var sourcePath = Path.Combine(directory, "Program.cs");
            await File.WriteAllTextAsync(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(sourcePath, """
                using System.Reflection;
                using System.Runtime.Loader;
                using System.Runtime.InteropServices;
                using System.Text;
                using System.Diagnostics;

                var mode = args[0];
                var helper = Environment.GetEnvironmentVariable("HRP_MAINTENANCE_HELPER_DLL")!;
                if (mode == "relay-bootstrap")
                {
                    var relay = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true,
                        RedirectStandardError = true, CreateNoWindow = true };
                    relay.ArgumentList.Add(helper);
                    relay.ArgumentList.Add("bootstrap");
                    using var nested = Process.Start(relay)!;
                    await nested.WaitForExitAsync();
                    if (nested.ExitCode != 10) return 11;
                    Console.Write("rejected-by-actual-parent-check");
                    return 0;
                }

                var apiPath = Environment.GetEnvironmentVariable("HRP_MAINTENANCE_API_DLL")!;
                var apiDirectory = Path.GetDirectoryName(apiPath)!;
                AssemblyLoadContext.Default.Resolving += (_, name) =>
                {
                    var dependency = Path.Combine(apiDirectory, name.Name + ".dll");
                    return File.Exists(dependency) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency) : null;
                };
                var api = AssemblyLoadContext.Default.LoadFromAssemblyPath(apiPath);
                var sqliteProviderPath = Path.Combine(apiDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll");
                if (File.Exists(sqliteProviderPath))
                {
                    var provider = AssemblyLoadContext.Default.LoadFromAssemblyPath(sqliteProviderPath);
                    NativeLibrary.SetDllImportResolver(provider, (name, _, _) =>
                    {
                        if (name != "e_sqlite3") return IntPtr.Zero;
                        var nativeName = OperatingSystem.IsWindows() ? "e_sqlite3.dll"
                            : OperatingSystem.IsMacOS() ? "libe_sqlite3.dylib" : "libe_sqlite3.so";
                        var nativePath = Path.Combine(apiDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier,
                            "native", nativeName);
                        return File.Exists(nativePath) ? NativeLibrary.Load(nativePath) : IntPtr.Zero;
                    });
                }
                var flags = BindingFlags.NonPublic | BindingFlags.Static;
                var paths = Encoding.UTF8.GetString(Convert.FromBase64String(
                    Environment.GetEnvironmentVariable("HRP_HORSE_MAINTENANCE_CHILD_SOURCES")!)).Split('\n');
                var token = Environment.GetEnvironmentVariable("HRP_INTERNAL_HORSE_MIGRATION_AUTH");
                var capabilityType = api.GetType("HorseRacingPrediction.Api.IdentityMigration.HorseIdentityMigrationMaintenanceCapability", true)!;
                var bootstrapType = capabilityType.GetNestedType("ApiBootstrapAuthorization", BindingFlags.NonPublic)!;
                var operationType = capabilityType.GetNestedType("OperationAuthorization", BindingFlags.NonPublic)!;
                var authType = mode == "install-cli" ? operationType : bootstrapType;
                var import = authType.GetMethod("TryImportAndValidate", flags)!;
                object?[] importArgs = [token, paths, null];
                if (!(bool)import.Invoke(null, importArgs)!) return 10;
                var authorization = importArgs[2]!;
                if (mode == "bootstrap") { Console.Write("bootstrap-validated"); return 0; }

                var runtimeType = api.GetType("HorseRacingPrediction.Api.IdentityMigration.HorseIdentityMigrationRuntimeOwnership", true)!;
                object runtime;
                if (mode == "install-cli")
                {
                    var acquire = runtimeType.GetMethod("AcquireForOperation", flags)!;
                    runtime = acquire.Invoke(null, [paths, authorization])!;
                    try
                    {
                        runtimeType.GetMethod("Acquire", flags)!.Invoke(null, [paths]);
                        return 12;
                    }
                    catch (TargetInvocationException exception) when (exception.InnerException is IOException) { }
                    ((IDisposable)runtime).Dispose();
                    Console.Write("install-cli-validated");
                    return 0;
                }

                if (mode != "install-api") return 13;
                runtime = runtimeType.GetMethod("Acquire", flags)!.Invoke(null, [paths])!;
                try
                {
                    var journalType = api.GetType("HorseRacingPrediction.Api.IdentityMigration.HorseIdentityMigrationJournal", true)!;
                    var constructor = journalType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Single(candidate => candidate.GetParameters().Length >= 2
                            && candidate.GetParameters()[0].ParameterType == typeof(string));
                    var constructorArgs = constructor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
                    constructorArgs[0] = Environment.GetEnvironmentVariable("HRP_HORSE_MAINTENANCE_CHILD_JOURNAL")!;
                    constructorArgs[1] = paths;
                    var journal = constructor.Invoke(constructorArgs);
                    var install = api.GetType("HorseRacingPrediction.Api.IdentityMigration.HorseIdentityMigrationMaintenanceOwnership", true)!
                        .GetMethod("InstallMaintenanceGatesAsync", flags)!;
                    var operation = Guid.Parse(Environment.GetEnvironmentVariable("HRP_HORSE_MAINTENANCE_CHILD_OPERATION")!);
                    var hash = Environment.GetEnvironmentVariable("HRP_HORSE_MAINTENANCE_CHILD_HASH")!;
                    var task = (Task)install.Invoke(null, [runtime, authorization, operation, hash, journal, CancellationToken.None])!;
                    await task;
                }
                finally { ((IDisposable)runtime).Dispose(); }
                Console.Write("install-api-validated");
                return 0;

                """);

            var build = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            build.ArgumentList.Add("build");
            build.ArgumentList.Add(projectPath);
            build.ArgumentList.Add("-c");
            build.ArgumentList.Add("Release");
            build.ArgumentList.Add("--nologo");
            using var process = Process.Start(build) ?? throw new IOException("Could not build the direct maintenance child helper.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException($"The direct child test helper failed to build: {output} {error}");
            _maintenanceChildAssemblyPath = Path.Combine(directory, "bin", "Release", "net10.0", "MaintenanceChild.dll");
            return _maintenanceChildAssemblyPath;
        }
        finally
        {
            MaintenanceChildBuildGate.Release();
        }
    }

    private static string TamperLastBase64Field(string encoded)
    {
        var fields = Encoding.ASCII.GetString(Convert.FromBase64String(encoded)).Split('\n');
        var last = fields[^1];
        fields[^1] = last[..^1] + (last[^1] == '0' ? '1' : '0');
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Join('\n', fields)));
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath },
        }) ?? throw new IOException("The test could not start the junction creation helper.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException($"The test could not create a reparse-point counterexample: {output}{error}");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string linkPath, string existingPath, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix(string existingPath, string linkPath);

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameWindows(string longPath, StringBuilder shortPath, uint bufferLength);
}
