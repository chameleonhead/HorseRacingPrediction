using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationRuntimeOwnershipTests
{
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
