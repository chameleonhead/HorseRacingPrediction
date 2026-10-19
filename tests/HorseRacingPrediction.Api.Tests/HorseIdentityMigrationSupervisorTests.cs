using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Api.IdentityMigration;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationSupervisorTests
{
    [TestMethod]
    public async Task SupervisorModeIsRestrictedToLinuxPidOne()
    {
        Assert.IsTrue(HorseIdentityMigrationSupervisor.IsSupportedSupervisorProcess(isLinux: true, processId: 1));
        Assert.IsFalse(HorseIdentityMigrationSupervisor.IsSupportedSupervisorProcess(isLinux: false, processId: 1));
        Assert.IsFalse(HorseIdentityMigrationSupervisor.IsSupportedSupervisorProcess(isLinux: true, processId: 2));
        Assert.AreEqual(2, await HorseIdentityMigrationSupervisor.RunIfRequestedAsync(
            [HorseIdentityMigrationSupervisor.CommandName, "unexpected"]));
    }

    [TestMethod]
    public void PrivateChildArgumentsAreExactAndCannotCarryCallerCommands()
    {
        Assert.IsTrue(HorseIdentityMigrationSupervisor.IsApiChild(
            [HorseIdentityMigrationSupervisor.ApiChildArgument]));
        Assert.IsFalse(HorseIdentityMigrationSupervisor.IsApiChild(
            [HorseIdentityMigrationSupervisor.ApiChildArgument, "--urls", "http://127.0.0.1"]));
        Assert.IsTrue(HorseIdentityMigrationSupervisor.IsBlockedChild(
            [HorseIdentityMigrationSupervisor.BlockedChildArgument]));
        Assert.IsFalse(HorseIdentityMigrationSupervisor.IsBlockedChild(
            [HorseIdentityMigrationSupervisor.BlockedChildArgument, "arbitrary"]));
    }

    [TestMethod]
    public void ParentDeathSetupRejectsUnstableOrMismatchedParentIdentity()
    {
        Assert.IsFalse(HorseIdentityMigrationSupervisor.TryEstablishParentDeathSignal(
            Environment.ProcessId + 100_000, 1));
        Assert.IsFalse(HorseIdentityMigrationSupervisor.TryEstablishParentDeathSignal(
            Environment.ProcessId, 0));
    }

    [TestMethod]
    public async Task ExportProductionContainerFixtureWhenRequested()
    {
        var requestedDirectory = Environment.GetEnvironmentVariable("HRP_HORSE_IDENTITY_SUPERVISOR_FIXTURE_DIR");
        var pauseAgeRequested = Environment.GetEnvironmentVariable(
            "HRP_HORSE_IDENTITY_SUPERVISOR_FIXTURE_PAUSE_AGED");
        if (!string.IsNullOrWhiteSpace(pauseAgeRequested) && pauseAgeRequested != "1")
            throw new InvalidOperationException("The disposable fixture pause-age flag must be exactly '1'.");
        if (string.IsNullOrWhiteSpace(requestedDirectory))
        {
            if (!string.IsNullOrWhiteSpace(pauseAgeRequested))
                throw new InvalidOperationException("The disposable fixture pause-age flag requires a fixture export directory.");
            return;
        }

        var outputDirectory = Path.GetFullPath(requestedDirectory);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!outputDirectory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The disposable runtime fixture must be written under the OS temporary directory.");
        Directory.CreateDirectory(outputDirectory);

        const string oldHorseId = "horse-11111111-1111-4111-8111-111111111111";
        const string raceId = "202609190101";
        const string ticketId = "predictionticket-00000000-0000-4000-8000-000000000201";
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await stores.SeedHorseAsync(oldHorseId);
        await stores.SeedJraResourceAsync(oldHorseId);
        var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, oldHorseId);
        await stores.SeedInterleavedTicketStreamAcrossPageBoundaryAsync(entryId);
        await HorseIdentityMigrationRuntimeModeTests.ReplaceSupportedEventsWithCommandBusSeedAsync(
            stores, oldHorseId, raceId, ticketId, entryId);

        var collectionStore = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = Path.GetDirectoryName(stores.CollectionPath)!,
            DatabaseFileName = Path.GetFileName(stores.CollectionPath),
        }));
        var pauseStartedAt = DateTimeOffset.UtcNow;
        if (pauseAgeRequested == "1") pauseStartedAt -= TimeSpan.FromMinutes(16);
        await collectionStore.SetPausedAsync(true, "isolated supervisor fixture", pauseStartedAt);
        var pipeline = await collectionStore.GetPipelineStateAsync();
        if (!pipeline.IsPaused || pipeline.UpdatedAt is null)
            throw new InvalidOperationException("The disposable fixture did not persist its collection pause timestamp.");

        File.Copy(stores.EventStorePath, Path.Combine(outputDirectory, "eventstore.db"));
        File.Copy(stores.CollectionPath, Path.Combine(outputDirectory, "collection-platform.db"));
        File.Copy(stores.SchedulePath, Path.Combine(outputDirectory, "prediction-executions.db"));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "pause-start.txt"),
            pipeline.UpdatedAt.Value.ToUniversalTime().ToString("O"));
    }

    [TestMethod]
    public async Task VerifiedJournalWithLeftoverPartialGateStartsOnlyTheMinimalHost()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hrp-supervisor-leftover-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var paths = new[]
            {
                Path.Combine(root, "eventstore.db"),
                Path.Combine(root, "collection.db"),
            };
            Assert.IsFalse(HorseIdentityMigrationSupervisor.ShouldStartBlockedHost(
                HorseIdentityMigrationStartupState.Ready, hasActiveIntent: false, paths));
            await File.WriteAllTextAsync(
                HorseIdentityMigrationMaintenanceOwnership.GetMaintenanceGatePath(paths[0]), "partial marker");

            // A completed/Verified journal can have no active intent while a crash leaves some gates behind.
            Assert.IsTrue(HorseIdentityMigrationSupervisor.ShouldStartBlockedHost(
                HorseIdentityMigrationStartupState.Ready, hasActiveIntent: false, paths),
                "Any remaining gate, even a malformed partial set, must select the journal-only blocked host.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ProductionParentDeathSignalTerminatesChildWhenOnlyItsOwnedParentDies()
        => await RunProductionParentDeathProbeAsync(creatorThreadRetires: false).ConfigureAwait(false);

    [TestMethod]
    public async Task ProductionParentDeathSignalSurvivesCallerThreadRetirement()
        => await RunProductionParentDeathProbeAsync(creatorThreadRetires: true).ConfigureAwait(false);

    private async Task RunProductionParentDeathProbeAsync(bool creatorThreadRetires)
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("The production PR_SET_PDEATHSIG and /proc thread-ownership proof requires Linux.");

        var root = Path.Combine(Path.GetTempPath(), $"hrp-parent-death-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var markerPath = Path.Combine(root, "child-ready.txt");
        var callerThreadPath = Path.Combine(root, "caller-thread.txt");
        var projectPath = Path.Combine(root, "ParentDeathProbe.csproj");
        var sourcePath = Path.Combine(root, "Program.cs");
        var helperPath = Path.Combine(root, "bin", "Release", "net10.0", "ParentDeathProbe.dll");
        var apiAssemblyPath = typeof(HorseIdentityMigrationSupervisor).Assembly.Location;
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
            </Project>
            """).ConfigureAwait(false);
        await File.WriteAllTextAsync(sourcePath, """
            using System.Diagnostics;
            using System.Reflection;
            using System.Runtime.InteropServices;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            const string supervisorTypeName = "HorseRacingPrediction.Api.IdentityMigration.HorseIdentityMigrationSupervisor";
            const string ownershipTypeName = "HorseRacingPrediction.Api.IdentityMigration.HorseIdentityMigrationMaintenanceOwnership";
            var role = args[0];
            var api = Assembly.LoadFrom(args[1]);
            var supervisor = api.GetType(supervisorTypeName, throwOnError: true)!;
            var ownership = api.GetType(ownershipTypeName, throwOnError: true)!;
            var getTicks = ownership.GetMethod("GetProcessStartTicks", flags)!;
            if (role is "parent" or "thread-parent")
            {
                var parentId = Environment.ProcessId;
                var startTicks = (long)getTicks.Invoke(null, [parentId])!;
                var startOwnedChild = supervisor.GetMethod("StartOwnedChild", flags)!;
                object? ownedChild = null;
                var callerThreadId = 0;
                void StartOwnedChildFromCallerThread()
                {
                    callerThreadId = Native.GetThreadId();
                    File.WriteAllText(args[3], callerThreadId.ToString());
                    var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
                    startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                    startInfo.ArgumentList.Add("child");
                    startInfo.ArgumentList.Add(args[1]);
                    startInfo.ArgumentList.Add(args[2]);
                    startInfo.ArgumentList.Add(args[3]);
                    startInfo.ArgumentList.Add(parentId.ToString());
                    startInfo.ArgumentList.Add(startTicks.ToString());
                    ownedChild = startOwnedChild.Invoke(null, [startInfo, false]);
                    var readyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                    while (!File.Exists(args[2]) && DateTime.UtcNow < readyDeadline) Thread.Sleep(10);
                }

                if (role == "thread-parent")
                {
                    var callerThread = new Thread(StartOwnedChildFromCallerThread);
                    callerThread.Start();
                    if (!callerThread.Join(TimeSpan.FromSeconds(35))) return 6;
                }
                else
                {
                    StartOwnedChildFromCallerThread();
                }
                if (ownedChild is null || callerThreadId <= 0) return 5;
                Thread.Sleep(Timeout.Infinite);
            }
            if (role != "child") return 2;
            var expectedParent = int.Parse(args[4]);
            var expectedStartTicks = long.Parse(args[5]);
            var actualParent = Native.GetParentProcessId();
            if (actualParent != expectedParent) return 3;
            var establish = supervisor.GetMethod("TryEstablishParentDeathSignal", flags)!;
            var staleAccepted = (bool)establish.Invoke(null, [expectedParent, expectedStartTicks + 1])!;
            var accepted = (bool)establish.Invoke(null, [expectedParent, expectedStartTicks])!;
            if (staleAccepted || !accepted) return 4;
            var actualStartTicks = (long)getTicks.Invoke(null, [expectedParent])!;
            var creatorTid = 0;
            foreach (var taskDirectory in Directory.EnumerateDirectories($"/proc/{expectedParent}/task"))
            {
                var candidateTid = Path.GetFileName(taskDirectory);
                var childrenPath = Path.Combine(taskDirectory, "children");
                if (File.Exists(childrenPath) && File.ReadAllText(childrenPath)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(Environment.ProcessId.ToString()))
                {
                    creatorTid = int.Parse(candidateTid);
                    break;
                }
            }
            if (creatorTid <= 0) return 7;
            File.WriteAllText(args[2], $"{Environment.ProcessId}\n{actualParent}\n{actualStartTicks}\n{creatorTid}");
            Thread.Sleep(Timeout.Infinite);
            return 0;

            static class Native
            {
                [DllImport("libc", EntryPoint = "getppid")] internal static extern int GetParentProcessId();
                [DllImport("libc", EntryPoint = "gettid")] internal static extern int GetThreadId();
            }
            """).ConfigureAwait(false);

        using var build = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };
        foreach (var argument in new[] { "build", projectPath, "-c", "Release", "--nologo", "--verbosity", "quiet" })
            build.StartInfo.ArgumentList.Add(argument);
        build.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        build.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        var output = new BoundedProcessOutput(16 * 1024);
        build.OutputDataReceived += (_, eventArgs) => output.Append(eventArgs.Data);
        build.ErrorDataReceived += (_, eventArgs) => output.Append(eventArgs.Data);
        Assert.IsTrue(build.Start(), "The direct-parent probe did not build.");
        build.BeginOutputReadLine();
        build.BeginErrorReadLine();
        build.StandardInput.Close();
        using (var buildTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            await build.WaitForExitAsync(buildTimeout.Token).ConfigureAwait(false);
        Assert.AreEqual(0, build.ExitCode, $"The direct-parent probe build failed. {output.Text}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { helperPath, creatorThreadRetires ? "thread-parent" : "parent",
                     apiAssemblyPath, markerPath, callerThreadPath })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var parent = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        parent.OutputDataReceived += (_, eventArgs) => output.Append(eventArgs.Data);
        parent.ErrorDataReceived += (_, eventArgs) => output.Append(eventArgs.Data);
        var childProcessId = 0;
        try
        {
            Assert.IsTrue(parent.Start(), "The disposable direct helper parent did not start.");
            parent.BeginOutputReadLine();
            parent.BeginErrorReadLine();
            parent.StandardInput.Close();
            var markerDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            while (!File.Exists(markerPath) && DateTimeOffset.UtcNow < markerDeadline)
            {
                Assert.IsFalse(parent.HasExited,
                    $"The helper parent exited before its child became ready. {output.Text}");
                await Task.Delay(50).ConfigureAwait(false);
            }
            Assert.IsTrue(File.Exists(markerPath), $"The production-owned child did not prove parent identity in time. {output.Text}");
            var identity = await File.ReadAllLinesAsync(markerPath).ConfigureAwait(false);
            Assert.AreEqual(4, identity.Length);
            childProcessId = int.Parse(identity[0], System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(parent.Id, int.Parse(identity[1], System.Globalization.CultureInfo.InvariantCulture),
                "The child must be directly owned by the disposable helper process we terminate.");
            Assert.IsTrue(long.Parse(identity[2], System.Globalization.CultureInfo.InvariantCulture) > 0,
                "The child must validate the kernel's stable parent start identity.");
            var productionCreatorTid = int.Parse(identity[3], System.Globalization.CultureInfo.InvariantCulture);

            if (creatorThreadRetires)
            {
                var callerThreadId = int.Parse(await File.ReadAllTextAsync(callerThreadPath).ConfigureAwait(false),
                    System.Globalization.CultureInfo.InvariantCulture);
                var callerRetirementDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
                while (Directory.Exists($"/proc/{parent.Id}/task/{callerThreadId}")
                    && DateTimeOffset.UtcNow < callerRetirementDeadline)
                    await Task.Delay(20).ConfigureAwait(false);
                Assert.IsFalse(Directory.Exists($"/proc/{parent.Id}/task/{callerThreadId}"),
                    "The external caller thread must retire after the child has installed the production guard.");
                Assert.AreNotEqual(callerThreadId, productionCreatorTid,
                    "StartOwnedChild must create the child on its retained owner thread, not the short-lived caller.");
                var parentThreads = Directory.Exists($"/proc/{parent.Id}/task")
                    ? string.Join(',', Directory.EnumerateDirectories($"/proc/{parent.Id}/task")
                        .Select(Path.GetFileName))
                    : "parent-exited";
                var childState = File.Exists($"/proc/{childProcessId}/stat")
                    ? (await File.ReadAllTextAsync($"/proc/{childProcessId}/stat").ConfigureAwait(false))
                        .Split(')', 2).LastOrDefault()?.TrimStart().FirstOrDefault().ToString()
                    : "missing";
                var childExit = "unavailable";
                try
                {
                    using var child = Process.GetProcessById(childProcessId);
                    if (child.HasExited) childExit = child.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (ArgumentException) { childExit = "reaped"; }
                Assert.IsTrue(Directory.Exists($"/proc/{parent.Id}/task/{productionCreatorTid}"),
                    $"The production Process.Start thread must remain alive until its child exits. Parent PID {parent.Id}, child PID {childProcessId}, creator TID {productionCreatorTid}, caller TID {callerThreadId}, parent threads [{parentThreads}], child state {childState}, child exit {childExit}.");
            }

            Assert.IsFalse(parent.HasExited, "The original parent process must remain alive before the parent-death assertion.");
            try
            {
                using var child = Process.GetProcessById(childProcessId);
                Assert.IsFalse(child.HasExited,
                    $"The guarded production child must be running while its parent is alive (PID {childProcessId}, creator TID {productionCreatorTid}).");
                var processStat = await File.ReadAllTextAsync($"/proc/{childProcessId}/stat").ConfigureAwait(false);
                var stateStart = processStat.LastIndexOf(')') + 2;
                Assert.IsTrue(stateStart < processStat.Length && processStat[stateStart] != 'Z',
                    "The child must be running, not merely present as a zombie, while the original parent remains alive.");
            }
            catch (ArgumentException)
            {
                Assert.Fail($"The production-owned child exited before its parent. Parent PID {parent.Id}, child PID {childProcessId}, creator TID {productionCreatorTid}.");
            }

            parent.Kill(entireProcessTree: false);
            using (var parentDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                await parent.WaitForExitAsync(parentDeadline.Token).ConfigureAwait(false);
            await WaitForProcessToDisappearAsync(childProcessId, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            if (!parent.HasExited)
            {
                parent.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await parent.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
            if (childProcessId > 0)
                await TerminateIfStillAliveAsync(childProcessId).ConfigureAwait(false);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void SourceCapacityDoesNotSplitFilesAcrossDifferentMountNames()
    {
        const long mebibyte = 1024 * 1024;
        var bindMountCounterexample = new (long Length, long AvailableBytes)[]
        {
            (40 * mebibyte, 150 * mebibyte),
            (40 * mebibyte, 150 * mebibyte),
        };

        Assert.IsFalse(HorseIdentityMigrationSupervisor.HasRequiredSourceFilesystemCapacity(bindMountCounterexample),
            "A per-mount-name check would accept 144 MiB twice even though both paths can share one filesystem that needs 224 MiB.");
        Assert.IsTrue(HorseIdentityMigrationSupervisor.HasRequiredSourceFilesystemCapacity(
            [(40 * mebibyte, 224 * mebibyte), (40 * mebibyte, 224 * mebibyte)]));
    }

    private static async Task WaitForProcessToDisappearAsync(int processId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) return;
            }
            catch (ArgumentException) { return; }
            await Task.Delay(50).ConfigureAwait(false);
        }
        Assert.Fail($"Parent-death signal did not terminate owned child process {processId}.");
    }

    private static async Task TerminateIfStillAliveAsync(int processId)
    {
        try
        {
            using var child = Process.GetProcessById(processId);
            if (child.HasExited) return;
            child.Kill(entireProcessTree: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (ArgumentException) { }
    }

    private static int GetParentProcessId()
    {
        var processId = GetParentProcessIdNative();
        if (processId <= 0) throw new InvalidOperationException("The disposable child has no valid parent process.");
        return processId;
    }

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int GetParentProcessIdNative();

    private sealed class BoundedProcessOutput(int maximumLength)
    {
        private readonly StringBuilder _text = new(maximumLength);
        private readonly object _sync = new();
        internal string Text { get { lock (_sync) return _text.ToString(); } }
        internal void Append(string? line)
        {
            if (line is null) return;
            lock (_sync)
            {
                if (_text.Length >= maximumLength) return;
                _text.Append(line.AsSpan(0, Math.Min(line.Length, maximumLength - _text.Length))).AppendLine();
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
