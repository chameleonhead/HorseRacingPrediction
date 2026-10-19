using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using EventFlow.Extensions;
using EventFlow.Aggregates;
using EventFlow.EventStores;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Races;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.IdentityMigration;

/// <summary>PID-1 coordinator for the only production-supported supervised migration runtime.</summary>
internal static class HorseIdentityMigrationSupervisor
{
    internal const string CommandName = "supervise";
    internal const string ApiChildArgument = "--internal-api-child";
    internal const string BlockedChildArgument = "--internal-blocked-child";
    internal const int VerifyReadyExitCode = 74;
    private const int PrSetPdeathsig = 1;
    private const int SigTerm = 15;
    private const long SourceCapacityReserveBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan FixedCliChildTimeout = TimeSpan.FromMinutes(31);
    private static readonly TimeSpan ChildTerminationGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ChildReapTimeout = TimeSpan.FromSeconds(10);

    internal static bool IsApiChild(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 && arguments[0] == ApiChildArgument;

    internal static bool IsBlockedChild(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 && arguments[0] == BlockedChildArgument;

    internal static bool IsSupportedSupervisorProcess(bool isLinux, int processId) => isLinux && processId == 1;

    internal static bool ShouldStartBlockedHost(HorseIdentityMigrationStartupState startupState,
        bool hasActiveIntent, IEnumerable<string> sourcePaths)
    {
        if (startupState != HorseIdentityMigrationStartupState.Ready || hasActiveIntent) return true;
        foreach (var sourcePath in sourcePaths)
        {
            try
            {
                _ = File.GetAttributes(HorseIdentityMigrationMaintenanceOwnership.GetMaintenanceGatePath(sourcePath));
                return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or System.Security.SecurityException)
            {
                // An unreadable marker must never allow the normal API to start.
                return true;
            }
        }
        return false;
    }

    internal static async Task<int?> RunIfRequestedAsync(string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] != CommandName) return null;
        if (arguments.Length != 1 || !IsSupportedSupervisorProcess(OperatingSystem.IsLinux(), Environment.ProcessId))
            return 2;
        try
        {
            return await RunSupervisorAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return 5;
        }
    }

    internal static bool TryEstablishParentDeathSignal(int expectedParentProcessId, long expectedParentStartTicks)
    {
        if (!OperatingSystem.IsLinux() || expectedParentProcessId <= 0 || expectedParentStartTicks <= 0)
            return false;
        try
        {
            if (GetParentProcessId() != expectedParentProcessId
                || HorseIdentityMigrationMaintenanceOwnership.GetProcessStartTicks(expectedParentProcessId)
                    != expectedParentStartTicks)
                return false;
            if (Prctl(PrSetPdeathsig, SigTerm, 0, 0, 0) != 0) return false;
            return GetParentProcessId() == expectedParentProcessId
                && HorseIdentityMigrationMaintenanceOwnership.GetProcessStartTicks(expectedParentProcessId)
                    == expectedParentStartTicks;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
            or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<int> RunSupervisorAsync()
    {
        using var stopping = new CancellationTokenSource();
        using var signalRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            stopping.Cancel();
        });
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        var sources = HorseIdentityMigrationRuntimeMode.ResolveConfiguredSources(builder,
            HorseIdentityMigrationRuntimeMode.ReadCollectionOptions(builder));
        var paths = sources.PhysicalSourcePaths;
        using var controlLease = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(paths);
        var journal = HorseIdentityMigrationRuntimeMode.CreateJournal(builder);

        var startup = await journal.InspectStartupGuardAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
        var active = await journal.GetActiveExecutionIntentAsync().ConfigureAwait(false);
        var startBlocked = ShouldStartBlockedHost(startup.State, active is not null, paths);

        while (true)
        {
            if (stopping.IsCancellationRequested) return 143;
            using var child = StartApiChild(startBlocked, controlLease);
            int exitCode;
            try
            {
                exitCode = await WaitForChildExitAsync(child, cancellationToken: stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return 143;
            }
            if (stopping.IsCancellationRequested) return 143;

            if (exitCode == HorseIdentityMigrationPreparationService.ApplyReadyExitCode)
            {
                if (!await ApplyPreparedOperationAsync(sources, paths, journal, controlLease, stopping.Token)
                        .ConfigureAwait(false))
                    return 5;
                if (stopping.IsCancellationRequested) return 143;
                startBlocked = false;
                continue;
            }

            if (exitCode == VerifyReadyExitCode)
            {
                if (!await VerifyReadonlyOperationAsync(sources, paths, journal, controlLease, stopping.Token)
                        .ConfigureAwait(false))
                    return 5;
                if (stopping.IsCancellationRequested) return 143;
                startBlocked = false;
                continue;
            }

            // A normal stop or unexpected child exit is not permission to replay or restart work.
            return exitCode == 0 ? 5 : exitCode;
        }
    }

    private static OwnedChild StartApiChild(bool blocked,
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease)
    {
        var bootstrap = controlLease.IssueApiBootstrapAuthorization();
        var startInfo = CreateDotnetStartInfo(blocked ? BlockedChildArgument : ApiChildArgument);
        startInfo.Environment[HorseIdentityMigrationMaintenanceCapability.PrivateEnvironmentVariable] =
            bootstrap.SerializeForPrivateEnvironment();
        return StartOwnedChild(startInfo, captureOutput: false);
    }

    private static async Task<bool> ApplyPreparedOperationAsync(
        HorseIdentityMigrationConfiguredSources sources, IReadOnlyList<string> paths,
        HorseIdentityMigrationJournal journal,
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease,
        CancellationToken stoppingToken)
    {
        var intent = await journal.GetActiveExecutionIntentAsync().ConfigureAwait(false);
        if (intent is null || intent.Phase != HorseIdentityMigrationExecutionPhase.StoppingApi
            || !HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(paths, intent.OperationId,
                intent.ImmutableRequestHash, HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(paths)))
            return false;
        var operation = await journal.GetStatusAsync(intent.OperationId).ConfigureAwait(false);
        if (operation?.Status != HorseIdentityMigrationOperationStatus.Staged) return false;

        if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.ApiExited,
                DateTimeOffset.UtcNow).ConfigureAwait(false)).Succeeded)
            return false;

        var backup = HorseIdentityMigrationManagedBackup.ForJournal(journal);
        stoppingToken.ThrowIfCancellationRequested();
        await CreateOrValidateBackupAsync(backup, intent.OperationId, sources, journal, stoppingToken)
            .ConfigureAwait(false);
        stoppingToken.ThrowIfCancellationRequested();
        EnsureSourceFilesystemCapacity(sources.PhysicalSourcePaths);
        if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.BackedUp,
                DateTimeOffset.UtcNow).ConfigureAwait(false)).Succeeded
            || !(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.Applying,
                DateTimeOffset.UtcNow).ConfigureAwait(false)).Succeeded)
            return false;

        var operationAuthorization = await controlLease.IssueOperationAuthorizationAsync(intent.OperationId,
            intent.ImmutableRequestHash, journal, stoppingToken).ConfigureAwait(false);
        using (var cli = StartFixedCliChild(intent.OperationId, operationAuthorization))
            if (await WaitForChildExitAsync(cli, FixedCliChildTimeout, stoppingToken).ConfigureAwait(false) != 0) return false;
        stoppingToken.ThrowIfCancellationRequested();

        var completed = await journal.GetStatusAsync(intent.OperationId).ConfigureAwait(false);
        var dataIntent = await journal.GetExecutionIntentAsync(intent.OperationId).ConfigureAwait(false);
        if (completed?.Status != HorseIdentityMigrationOperationStatus.Succeeded
            || dataIntent?.Phase != HorseIdentityMigrationExecutionPhase.DataSucceeded
            || !await journal.CanBeginReadonlyVerificationAsync(intent.OperationId).ConfigureAwait(false))
            return false;

        if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.Verifying,
                DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false)).Succeeded)
            return false;
        await VerifyAllDataAsync(intent.OperationId, sources, journal, backup, stoppingToken)
            .ConfigureAwait(false);
        stoppingToken.ThrowIfCancellationRequested();
        if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.Verified,
                DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false)).Succeeded)
            return false;

        using var runtimeOwnership = HorseIdentityMigrationRuntimeOwnership.AcquireForMaintenanceGateManagement(
            paths, controlLease, operationAuthorization);
        await HorseIdentityMigrationMaintenanceOwnership.ClearMaintenanceGatesAsync(runtimeOwnership, controlLease,
            operationAuthorization, journal).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> VerifyReadonlyOperationAsync(
        HorseIdentityMigrationConfiguredSources sources, IReadOnlyList<string> paths,
        HorseIdentityMigrationJournal journal,
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease,
        CancellationToken stoppingToken)
    {
        var intent = await journal.GetActiveExecutionIntentAsync().ConfigureAwait(false);
        if (intent is null || intent.Phase != HorseIdentityMigrationExecutionPhase.Verifying
            || (await journal.GetStatusAsync(intent.OperationId).ConfigureAwait(false))?.Status
                != HorseIdentityMigrationOperationStatus.Succeeded
            || !HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(paths, intent.OperationId,
                intent.ImmutableRequestHash, HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(paths)))
            return false;

        var authorization = await controlLease.IssueReadonlyReverificationAuthorizationAsync(intent.OperationId,
            intent.ImmutableRequestHash, journal, stoppingToken).ConfigureAwait(false);
        if (authorization is null) return false;
        using var runtimeOwnership = await HorseIdentityMigrationRuntimeOwnership
            .AcquireForReadonlyReverificationAsync(paths, controlLease, authorization, journal, stoppingToken).ConfigureAwait(false);
        await VerifyAllDataAsync(intent.OperationId, sources, journal,
            HorseIdentityMigrationManagedBackup.ForJournal(journal), stoppingToken).ConfigureAwait(false);
        stoppingToken.ThrowIfCancellationRequested();
        if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId,
                HorseIdentityMigrationExecutionPhase.Verified, DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false)).Succeeded)
            return false;
        await HorseIdentityMigrationMaintenanceOwnership.ClearMaintenanceGatesAfterReadonlyReverificationAsync(
            runtimeOwnership, controlLease, authorization, journal).ConfigureAwait(false);
        return true;
    }

    internal static async Task VerifyAllDataAsync(Guid operationId, HorseIdentityMigrationConfiguredSources sources,
        HorseIdentityMigrationJournal journal, HorseIdentityMigrationManagedBackup backup,
        CancellationToken cancellationToken)
    {
        var saved = await journal.GetSavedRequestAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The saved migration request is unavailable.");
        var validation = HorseIdentityMigrationManifestPolicy.Validate(saved.Manifest);
        if (!validation.IsValid) throw new InvalidDataException("The saved migration mapping is invalid.");
        using var services = new ServiceCollection().AddLogging().AddEventFlow(options => options
            .AddDefaults(typeof(HorseAggregate).Assembly).AddDefaults(typeof(RaceAggregate).Assembly))
            .BuildServiceProvider();
        var verifier = new HorseIdentityMigrationPostVerification(backup,
            services.GetRequiredService<IEventJsonSerializer>(), services.GetRequiredService<IDomainEventFactory>());
        await verifier.VerifyAsync(operationId, sources, journal, validation.Mappings, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task CreateOrValidateBackupAsync(HorseIdentityMigrationManagedBackup backup,
        Guid operationId, HorseIdentityMigrationConfiguredSources sources, HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken)
    {
        var stateDirectory = Path.GetDirectoryName(journal.DatabasePath)
            ?? throw new IOException("The migration journal has no state directory.");
        var packagePath = Path.Combine(stateDirectory, "backups", $"horse-id-migration-{operationId:N}");
        if (Directory.Exists(packagePath) || File.Exists(packagePath))
            await backup.ValidateAsync(operationId, sources, journal, cancellationToken).ConfigureAwait(false);
        else
            await backup.CreateAsync(operationId, sources, journal, cancellationToken).ConfigureAwait(false);
    }

    private static OwnedChild StartFixedCliChild(Guid operationId,
        HorseIdentityMigrationMaintenanceCapability.OperationAuthorization authorization)
    {
        var startInfo = CreateDotnetStartInfo(HorseIdentityMigrationRuntimeMode.CommandName, operationId.ToString("D"));
        startInfo.Environment[HorseIdentityMigrationMaintenanceCapability.PrivateEnvironmentVariable] =
            authorization.SerializeForPrivateEnvironment();
        return StartOwnedChild(startInfo, captureOutput: true);
    }

    private static OwnedChild StartOwnedChild(ProcessStartInfo startInfo, bool captureOutput)
    {
        startInfo.RedirectStandardOutput = captureOutput;
        startInfo.RedirectStandardError = captureOutput;
        var processStarted = new TaskCompletionSource<Process>(TaskCreationOptions.RunContinuationsAsynchronously);
        var creatorThreadLifetime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creatorThread = new Thread(() =>
        {
            Process? process = null;
            var processDidStart = false;
            try
            {
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                if (!process.Start()) throw new InvalidOperationException("The supervised child process did not start.");
                processDidStart = true;
                processStarted.TrySetResult(process);

                // Linux PR_SET_PDEATHSIG is tied to the OS thread that created this child.
                // Keep that exact thread alive until the child has exited and been reaped.
                process.WaitForExit();
                creatorThreadLifetime.TrySetResult();
            }
            catch (Exception exception)
            {
                if (!processDidStart)
                {
                    process?.Dispose();
                    processStarted.TrySetException(exception);
                    creatorThreadLifetime.TrySetResult();
                }
                else
                {
                    creatorThreadLifetime.TrySetException(exception);
                }
            }
        })
        {
            IsBackground = true,
            Name = "HorseIdentityMigrationOwnedChild",
        };
        creatorThread.Start();
        var process = processStarted.Task.GetAwaiter().GetResult();

        // Preserve the API's normal stdout/stderr diagnostics. Only the fixed one-shot CLI
        // is redirected; its output is drained without buffering or forwarding.
        return captureOutput
            ? new OwnedChild(process, creatorThreadLifetime.Task,
                DrainOutputAsync(process.StandardOutput), DrainOutputAsync(process.StandardError))
            : new OwnedChild(process, creatorThreadLifetime.Task, Task.CompletedTask, Task.CompletedTask);
    }

    private static async Task DrainOutputAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false) != 0) { }
    }

    private static async Task<int> WaitForChildExitAsync(OwnedChild child, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var deadline = timeout is null ? null : new CancellationTokenSource(timeout.Value);
        using var linked = deadline is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await child.Process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { _ = SendSignal(child.Process.Id, SigTerm); }
            catch (InvalidOperationException) { }
            using var grace = new CancellationTokenSource(ChildTerminationGrace);
            try { await child.Process.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { child.Process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                using var reapDeadline = new CancellationTokenSource(ChildReapTimeout);
                await child.Process.WaitForExitAsync(reapDeadline.Token).ConfigureAwait(false);
            }
            await Task.WhenAll(child.CreatorThreadLifetime, child.StandardOutputDrain, child.StandardErrorDrain)
                .ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (deadline?.IsCancellationRequested == true)
        {
            try { child.Process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            using var reapDeadline = new CancellationTokenSource(ChildReapTimeout);
            await child.Process.WaitForExitAsync(reapDeadline.Token).ConfigureAwait(false);
            await Task.WhenAll(child.CreatorThreadLifetime, child.StandardOutputDrain, child.StandardErrorDrain)
                .WaitAsync(reapDeadline.Token).ConfigureAwait(false);
            throw new TimeoutException("The supervised fixed migration CLI exceeded its execution bound.");
        }

        await Task.WhenAll(child.CreatorThreadLifetime, child.StandardOutputDrain, child.StandardErrorDrain)
            .ConfigureAwait(false);
        return child.Process.ExitCode;
    }

    private sealed class OwnedChild(Process process, Task creatorThreadLifetime,
        Task standardOutputDrain, Task standardErrorDrain) : IDisposable
    {
        internal Process Process { get; } = process;
        internal Task CreatorThreadLifetime { get; } = creatorThreadLifetime;
        internal Task StandardOutputDrain { get; } = standardOutputDrain;
        internal Task StandardErrorDrain { get; } = standardErrorDrain;
        public void Dispose() => Process.Dispose();
    }

    private static ProcessStartInfo CreateDotnetStartInfo(params string[] arguments)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The runtime executable is unavailable.");
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("The API assembly path is unavailable."));
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int processId, int signal);

    internal static bool HasRequiredSourceFilesystemCapacity(
        IReadOnlyList<(long Length, long AvailableBytes)> sources)
    {
        if (sources.Count == 0 || sources.Any(source => source.Length < 0 || source.AvailableBytes < 0)) return false;
        var totalSourceBytes = sources.Aggregate(0L, (sum, source) => checked(sum + source.Length));
        var required = checked(checked(totalSourceBytes * 2) + SourceCapacityReserveBytes);
        // Require the full configured source footprint on every source filesystem. This is
        // intentionally conservative: distinct bind-mount names can refer to one device.
        return sources.All(source => source.AvailableBytes >= required);
    }

    internal static void EnsureSourceFilesystemCapacity(IEnumerable<string> sourcePaths)
    {
        var capacities = new List<(long Length, long AvailableBytes)>();
        foreach (var path in sourcePaths)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("A configured migration source is unavailable.");
            var drive = FindDrive(path);
            capacities.Add((new FileInfo(path).Length, drive.AvailableFreeSpace));
        }
        if (!HasRequiredSourceFilesystemCapacity(capacities))
            throw new IOException("A configured source filesystem lacks the required migration headroom.");
    }

    private static DriveInfo FindDrive(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return DriveInfo.GetDrives().Where(drive => fullPath.StartsWith(
                Path.GetFullPath(drive.Name).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || string.Equals(fullPath, Path.GetFullPath(drive.Name),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .OrderByDescending(drive => drive.Name.Length).FirstOrDefault()
            ?? throw new IOException("The filesystem containing a configured migration source is unknown.");
    }

    [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static extern int Prctl(int option, int argument2, int argument3, int argument4, int argument5);

    [DllImport("libc", EntryPoint = "getppid", SetLastError = true)]
    private static extern int GetParentProcessId();
}
