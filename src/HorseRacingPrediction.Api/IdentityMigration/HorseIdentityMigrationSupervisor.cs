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
using Microsoft.AspNetCore.Builder;

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
    private static readonly TimeSpan OperatorRetryMaximumDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OperatorHealthDeadline = TimeSpan.FromSeconds(20);
    private const int InitialOperatorStartAttempts = 3;

    internal static bool IsApiChild(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 && arguments[0] == ApiChildArgument;

    internal static bool IsBlockedChild(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 && arguments[0] == BlockedChildArgument;

    internal static bool IsSupportedSupervisorProcess(bool isLinux, int processId) => isLinux && processId == 1;

    internal static bool ShouldStartBlockedHost(HorseIdentityMigrationStartupState startupState,
        bool hasActiveIntent, IEnumerable<string> sourcePaths, bool forceBlocked = false)
    {
        if (forceBlocked || startupState != HorseIdentityMigrationStartupState.Ready || hasActiveIntent) return true;
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

        var startBlocked = true;
        try
        {
            var startup = await journal.InspectStartupGuardAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
            var active = await journal.GetActiveExecutionIntentAsync().ConfigureAwait(false);
            startBlocked = ShouldStartBlockedHost(startup.State, active is not null, paths,
                HorseIdentityMigrationRuntimeMode.IsOperatorOnlyMode());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            // The minimal child can still keep authenticated navigation available and report
            // that journal state is unknown when startup inspection is corrupt or unavailable.
        }

        var operatorStartFailures = 0;
        while (true)
        {
            if (stopping.IsCancellationRequested) return 143;
            OwnedChild child;
            try
            {
                child = StartApiChild(startBlocked, controlLease);
            }
            catch
            {
                startBlocked = true;
                try
                {
                    var activeIntent = await journal.GetActiveExecutionIntentAsync(stopping.Token).ConfigureAwait(false);
                    if (activeIntent is not null)
                        await TryRecordFailureAsync(journal, activeIntent.OperationId,
                            HorseIdentityMigrationResultCode.StartupFailed, stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return 143;
                }
                catch
                {
                    // No operation-specific failure can be recorded if the journal is unavailable.
                }
                await WaitBeforeOperatorRetryAsync(++operatorStartFailures, stopping.Token).ConfigureAwait(false);
                continue;
            }
            using (child)
            {
                int exitCode;
                try
                {
                    exitCode = await WaitForChildExitAsync(child, cancellationToken: stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return 143;
                }
                catch
                {
                    // WaitForChildExitAsync already attempts termination/reap on control errors.
                    // Confirm ownership is fully settled again before treating the exit as an
                    // unexpected child failure or launching even the journal-only replacement.
                    await TerminateAndReapAsync(child).ConfigureAwait(false);
                    exitCode = -1;
                }
                if (stopping.IsCancellationRequested) return 143;

                if (HorseIdentityMigrationRuntimeMode.IsOperatorOnlyMode())
                {
                    startBlocked = true;
                    await WaitBeforeOperatorRetryAsync(++operatorStartFailures, stopping.Token).ConfigureAwait(false);
                    continue;
                }

                if (exitCode == HorseIdentityMigrationPreparationService.ApplyReadyExitCode)
                {
                    bool applied;
                    try
                    {
                        var outcome = await RunWorkWithOperatorHostAsync(
                            token => ApplyPreparedOperationAsync(builder, sources, paths, journal, controlLease, token),
                            controlLease, ResolveOperatorUrls(builder.Configuration), stopping.Token)
                            .ConfigureAwait(false);
                        applied = outcome ?? false;
                        if (outcome is null)
                            await RecordActiveFailureAsync(journal, HorseIdentityMigrationResultCode.StartupFailed,
                                stopping.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                    {
                        return 143;
                    }
                    catch (OperatorHostCleanupException)
                    {
                        throw;
                    }
                    catch
                    {
                        await RecordActiveFailureAsync(journal, HorseIdentityMigrationResultCode.StartupFailed,
                            stopping.Token).ConfigureAwait(false);
                        startBlocked = true;
                        continue;
                    }
                    if (!applied)
                    {
                        startBlocked = true;
                        continue;
                    }
                    if (stopping.IsCancellationRequested) return 143;
                    startBlocked = false;
                    operatorStartFailures = 0;
                    continue;
                }

                if (exitCode == VerifyReadyExitCode)
                {
                    bool verified;
                    try
                    {
                        var outcome = await RunWorkWithOperatorHostAsync(
                            token => VerifyReadonlyOperationAsync(sources, paths, journal, controlLease, token),
                            controlLease, ResolveOperatorUrls(builder.Configuration), stopping.Token)
                            .ConfigureAwait(false);
                        verified = outcome ?? false;
                        if (outcome is null)
                            await RecordActiveFailureAsync(journal, HorseIdentityMigrationResultCode.StartupFailed,
                                stopping.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                    {
                        return 143;
                    }
                    catch (OperatorHostCleanupException)
                    {
                        throw;
                    }
                    catch
                    {
                        verified = false;
                    }
                    if (!verified)
                    {
                        await RecordActiveFailureAsync(journal, HorseIdentityMigrationResultCode.VerificationFailed,
                            stopping.Token).ConfigureAwait(false);
                        startBlocked = true;
                        continue;
                    }
                    if (stopping.IsCancellationRequested) return 143;
                    startBlocked = false;
                    operatorStartFailures = 0;
                    continue;
                }

                // A child exit is never permission to replay work. Keep PID 1 alive on the
                // existing journal-only host, which has no business-store registrations.
                try
                {
                    var activeIntent = await journal.GetActiveExecutionIntentAsync(stopping.Token).ConfigureAwait(false);
                    if (activeIntent is not null)
                        await TryRecordFailureAsync(journal, activeIntent.OperationId,
                            HorseIdentityMigrationResultCode.ChildFailed, stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return 143;
                }
                catch
                {
                    // The status page reports unknown if the journal cannot be read.
                }
                startBlocked = true;
                await WaitBeforeOperatorRetryAsync(++operatorStartFailures, stopping.Token).ConfigureAwait(false);
            }
        }
    }

    private static async Task WaitBeforeOperatorRetryAsync(int failures, CancellationToken cancellationToken)
    {
        var seconds = Math.Min(OperatorRetryMaximumDelay.TotalSeconds,
            Math.Pow(2, Math.Min(Math.Max(failures - 1, 0), 4)));
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
    }

    internal static string ResolveOperatorUrls(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var urls = configuration["urls"];
        if (!string.IsNullOrWhiteSpace(urls)) return urls;
        var ports = configuration["http_ports"];
        if (!string.IsNullOrWhiteSpace(ports))
            return string.Join(';', ports.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(port => $"http://+:{port}"));
        return "http://localhost:5000";
    }

    internal static async Task<bool?> RunWorkWithOperatorHostAsync(
        Func<CancellationToken, Task<bool>> work,
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease,
        string operatorUrls, CancellationToken stoppingToken, Action<int>? onOperatorStarted = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorUrls);
        var healthUri = CreateOperatorHealthUri(operatorUrls);
        var child = await StartOperatorChildAsync(controlLease, operatorUrls, healthUri,
            InitialOperatorStartAttempts, stoppingToken, onOperatorStarted).ConfigureAwait(false);
        if (child is null) return null;

        using var workStopping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        Task<bool> workTask;
        try
        {
            workTask = work(workStopping.Token);
        }
        catch
        {
            await ReapOperatorChildAsync(child).ConfigureAwait(false);
            child.Dispose();
            throw;
        }

        var restartFailures = 0;
        try
        {
            while (!workTask.IsCompleted)
            {
                if (child is null)
                {
                    await WaitBeforeOperatorRetryAsync(++restartFailures, stoppingToken).ConfigureAwait(false);
                    child = await StartOperatorChildAsync(controlLease, operatorUrls, healthUri, 1,
                        stoppingToken, onOperatorStarted).ConfigureAwait(false);
                    continue;
                }
                var childExit = child.Process.WaitForExitAsync();
                if (await Task.WhenAny(workTask, childExit).ConfigureAwait(false) == workTask) break;

                try
                {
                    await WaitForChildExitAsync(child).ConfigureAwait(false);
                }
                catch
                {
                    await ReapOperatorChildAsync(child).ConfigureAwait(false);
                }
                child.Dispose();
                child = null;
                if (workTask.IsCompleted) break;

                await WaitBeforeOperatorRetryAsync(++restartFailures, stoppingToken).ConfigureAwait(false);
                child = await StartOperatorChildAsync(controlLease, operatorUrls, healthUri, 1,
                    stoppingToken, onOperatorStarted).ConfigureAwait(false);
            }

            return await workTask.ConfigureAwait(false);
        }
        catch
        {
            // Any unexpected operator-control failure cancels and settles the active work
            // delegate before this method can unwind. Its apply path owns and reaps the CLI.
            workStopping.Cancel();
            try { await workTask.ConfigureAwait(false); }
            catch { /* Preserve the control failure after the owned work has settled. */ }
            throw;
        }
        finally
        {
            if (child is not null)
            {
                await ReapOperatorChildAsync(child).ConfigureAwait(false);
                child.Dispose();
            }
        }
    }

    private static async Task<OwnedChild?> StartOperatorChildAsync(
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease,
        string operatorUrls, Uri healthUri, int attempts, CancellationToken stoppingToken,
        Action<int>? onOperatorStarted)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            stoppingToken.ThrowIfCancellationRequested();
            OwnedChild? child = null;
            try
            {
                child = StartApiChild(blocked: true, controlLease, operatorUrls, operatorOnly: true);
                onOperatorStarted?.Invoke(child.Process.Id);
                await WaitForOperatorHealthAsync(child, healthUri, stoppingToken).ConfigureAwait(false);
                return child;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (child is not null)
                {
                    await ReapOperatorChildAsync(child).ConfigureAwait(false);
                    child.Dispose();
                }
                throw;
            }
            catch
            {
                if (child is not null)
                {
                    await ReapOperatorChildAsync(child).ConfigureAwait(false);
                    child.Dispose();
                }
                if (attempt < attempts)
                    await WaitBeforeOperatorRetryAsync(attempt, stoppingToken).ConfigureAwait(false);
            }
        }
        return null;
    }

    private static async Task ReapOperatorChildAsync(OwnedChild child)
    {
        try
        {
            await TerminateAndReapAsync(child).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // An unconfirmed child must never be mistaken for an exited/reaped operator.
            // Let the PID-1 fail-closed path stop before any API restart.
            throw new OperatorHostCleanupException(exception);
        }
    }

    private sealed class OperatorHostCleanupException(Exception innerException)
        : Exception("The journal-only operator child could not be confirmed reaped.", innerException);

    private static async Task WaitForOperatorHealthAsync(OwnedChild child, Uri healthUri,
        CancellationToken stoppingToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(OperatorHealthDeadline);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        while (!deadline.IsCancellationRequested)
        {
            if (child.Process.HasExited)
                throw new InvalidOperationException("The journal-only operator child exited before becoming ready.");
            try
            {
                using var response = await client.GetAsync(healthUri, deadline.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested
                && !deadline.IsCancellationRequested)
            { }
            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token).ConfigureAwait(false);
        }
        stoppingToken.ThrowIfCancellationRequested();
        throw new TimeoutException("The journal-only operator child did not become ready within its bounded startup window.");
    }

    internal static Uri CreateOperatorHealthUri(string operatorUrls)
    {
        var address = operatorUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? throw new InvalidOperationException("The operator child has no configured URL.");
        address = address.Replace("://+:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://*:", "://127.0.0.1:", StringComparison.Ordinal);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("The operator child URL is invalid.");
        return new UriBuilder(baseUri) { Host = "127.0.0.1", Path = "/health", Query = string.Empty, Fragment = string.Empty }.Uri;
    }

    private static async Task TryRecordFailureAsync(HorseIdentityMigrationJournal journal, Guid operationId,
        HorseIdentityMigrationResultCode code, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await journal.RecordFailureAsync(operationId, code, DateTimeOffset.UtcNow, deadline.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or Microsoft.Data.Sqlite.SqliteException or OperationCanceledException)
        {
            // Failure metadata is best-effort when the bounded journal itself is unavailable.
        }
    }

    private static async Task RecordActiveFailureAsync(HorseIdentityMigrationJournal journal,
        HorseIdentityMigrationResultCode code, CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            var intent = await journal.GetActiveExecutionIntentAsync(deadline.Token).ConfigureAwait(false);
            if (intent is not null)
                await TryRecordFailureAsync(journal, intent.OperationId, code, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or Microsoft.Data.Sqlite.SqliteException or OperationCanceledException)
        {
            // Failure reporting must never escape PID 1 when the bounded journal is unavailable.
        }
    }

    private static OwnedChild StartApiChild(bool blocked,
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease,
        string? operatorUrls = null, bool operatorOnly = false)
    {
        var bootstrap = controlLease.IssueApiBootstrapAuthorization();
        var startInfo = CreateDotnetStartInfo(blocked ? BlockedChildArgument : ApiChildArgument);
        startInfo.Environment[HorseIdentityMigrationMaintenanceCapability.PrivateEnvironmentVariable] =
            bootstrap.SerializeForPrivateEnvironment();
        if (operatorUrls is not null) startInfo.Environment["ASPNETCORE_URLS"] = operatorUrls;
        if (operatorOnly)
            startInfo.Environment[HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable] = "true";
        return StartOwnedChild(startInfo, captureOutput: false);
    }

    private static async Task<bool> ApplyPreparedOperationAsync(
        WebApplicationBuilder builder, HorseIdentityMigrationConfiguredSources sources, IReadOnlyList<string> paths,
        HorseIdentityMigrationJournal journal,
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease,
        CancellationToken stoppingToken)
    {
        Guid? operationId = null;
        var failureCode = HorseIdentityMigrationResultCode.PreparationFailed;
        try
        {
            var intent = await journal.GetActiveExecutionIntentAsync(stoppingToken).ConfigureAwait(false);
            operationId = intent?.OperationId;
            if (intent is null || intent.Phase != HorseIdentityMigrationExecutionPhase.StoppingApi
                || !HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(paths, intent.OperationId,
                    intent.ImmutableRequestHash, HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(paths)))
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);
            var operation = await journal.GetStatusAsync(intent.OperationId, stoppingToken).ConfigureAwait(false);
            if (operation?.Status != HorseIdentityMigrationOperationStatus.Staged)
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);

            if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.ApiExited,
                    DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false)).Succeeded)
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);

            failureCode = HorseIdentityMigrationResultCode.BackupFailed;
            var backup = HorseIdentityMigrationManagedBackup.ForJournal(journal);
            stoppingToken.ThrowIfCancellationRequested();
            var backupReceipt = await CreateOrValidateBackupAsync(backup, intent.OperationId, sources, journal, stoppingToken)
                .ConfigureAwait(false);
            stoppingToken.ThrowIfCancellationRequested();
            EnsureSourceFilesystemCapacity(sources.PhysicalSourcePaths);
            if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.BackedUp,
                    DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false)).Succeeded)
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);

            failureCode = HorseIdentityMigrationResultCode.InspectionFailed;
            var executableRequest = await journal.GetExecutableRequestAsync(intent.OperationId, stoppingToken)
                .ConfigureAwait(false);
            if (executableRequest is null
                || !string.Equals(executableRequest.ImmutableRequestHash, intent.ImmutableRequestHash, StringComparison.Ordinal))
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);
            if (executableRequest.RequestKind == HorseIdentityMigrationAdmissionKind.PendingV2)
            {
                var sourceSetHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(paths);
                var (services, engine) = HorseIdentityMigrationRuntimeMode.CreateStoreEngine(builder);
                HorseIdentityMigrationPreview census;
                using (services)
                    census = await engine.InspectPreviewAsync(executableRequest.Manifest, stoppingToken).ConfigureAwait(false);
                if (!census.CanStage || census.Fingerprint is null)
                    return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);
                stoppingToken.ThrowIfCancellationRequested();
                var sealedResult = await journal.SealPendingRequestAsync(intent.OperationId,
                    executableRequest.ImmutableRequestHash, census.Fingerprint, sourceSetHash,
                    backupReceipt.ManifestHash, DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
                if (!sealedResult.Succeeded)
                    return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);
            }

            failureCode = HorseIdentityMigrationResultCode.ApplyFailed;
            if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.Applying,
                    DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false)).Succeeded)
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);

            failureCode = HorseIdentityMigrationResultCode.ChildFailed;
            var operationAuthorization = await controlLease.IssueOperationAuthorizationAsync(intent.OperationId,
                intent.ImmutableRequestHash, journal, stoppingToken).ConfigureAwait(false);
            using (var cli = StartFixedCliChild(intent.OperationId, operationAuthorization))
                if (await WaitForChildExitAsync(cli, FixedCliChildTimeout, stoppingToken).ConfigureAwait(false) != 0)
                    return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);
            stoppingToken.ThrowIfCancellationRequested();

            failureCode = HorseIdentityMigrationResultCode.ApplyFailed;
            var completed = await journal.GetStatusAsync(intent.OperationId, stoppingToken).ConfigureAwait(false);
            var dataIntent = await journal.GetExecutionIntentAsync(intent.OperationId, stoppingToken).ConfigureAwait(false);
            if (completed?.Status != HorseIdentityMigrationOperationStatus.Succeeded
                || dataIntent?.Phase != HorseIdentityMigrationExecutionPhase.DataSucceeded
                || !await journal.CanBeginReadonlyVerificationAsync(intent.OperationId, stoppingToken).ConfigureAwait(false))
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);

            failureCode = HorseIdentityMigrationResultCode.VerificationFailed;
            if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.Verifying,
                    DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false)).Succeeded)
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);
            await VerifyAllDataAsync(intent.OperationId, sources, journal, backup, stoppingToken)
                .ConfigureAwait(false);
            stoppingToken.ThrowIfCancellationRequested();
            if (!(await journal.AdvanceExecutionIntentAsync(intent.OperationId, HorseIdentityMigrationExecutionPhase.Verified,
                    DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false)).Succeeded)
                return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);

            using var runtimeOwnership = HorseIdentityMigrationRuntimeOwnership.AcquireForMaintenanceGateManagement(
                paths, controlLease, operationAuthorization);
            await HorseIdentityMigrationMaintenanceOwnership.ClearMaintenanceGatesAsync(runtimeOwnership, controlLease,
                operationAuthorization, journal).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await FailAsync(journal, operationId, failureCode, stoppingToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> FailAsync(HorseIdentityMigrationJournal journal, Guid? operationId,
        HorseIdentityMigrationResultCode code, CancellationToken cancellationToken)
    {
        if (operationId is { } value)
            await TryRecordFailureAsync(journal, value, code, cancellationToken).ConfigureAwait(false);
        return false;
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
        var request = await journal.GetExecutableRequestAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The saved migration request is unavailable.");
        var validation = HorseIdentityMigrationManifestPolicy.Validate(request.Manifest);
        if (!validation.IsValid) throw new InvalidDataException("The saved migration mapping is invalid.");
        using var services = new ServiceCollection().AddLogging().AddEventFlow(options => options
            .AddDefaults(typeof(HorseAggregate).Assembly).AddDefaults(typeof(RaceAggregate).Assembly))
            .BuildServiceProvider();
        var verifier = new HorseIdentityMigrationPostVerification(backup,
            services.GetRequiredService<IEventJsonSerializer>(), services.GetRequiredService<IDomainEventFactory>());
        await verifier.VerifyAsync(operationId, sources, journal, validation.Mappings, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<HorseIdentityMigrationBackupReceipt> CreateOrValidateBackupAsync(HorseIdentityMigrationManagedBackup backup,
        Guid operationId, HorseIdentityMigrationConfiguredSources sources, HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken)
    {
        var stateDirectory = Path.GetDirectoryName(journal.DatabasePath)
            ?? throw new IOException("The migration journal has no state directory.");
        var packagePath = Path.Combine(stateDirectory, "backups", $"horse-id-migration-{operationId:N}");
        if (Directory.Exists(packagePath) || File.Exists(packagePath))
            return await backup.ValidateAsync(operationId, sources, journal, cancellationToken).ConfigureAwait(false);
        return await backup.CreateAsync(operationId, sources, journal, cancellationToken).ConfigureAwait(false);
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
        catch
        {
            await TerminateAndReapAsync(child).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(child.CreatorThreadLifetime, child.StandardOutputDrain, child.StandardErrorDrain)
            .ConfigureAwait(false);
        return child.Process.ExitCode;
    }

    private static async Task TerminateAndReapAsync(OwnedChild child)
    {
        try
        {
            if (!child.Process.HasExited)
            {
                try { _ = SendSignal(child.Process.Id, SigTerm); }
                catch (InvalidOperationException) { }
                using var grace = new CancellationTokenSource(ChildTerminationGrace);
                try { await child.Process.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    try { child.Process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                }
            }
            using var reapDeadline = new CancellationTokenSource(ChildReapTimeout);
            await child.Process.WaitForExitAsync(reapDeadline.Token).ConfigureAwait(false);
            await Task.WhenAll(child.CreatorThreadLifetime, child.StandardOutputDrain, child.StandardErrorDrain)
                .WaitAsync(reapDeadline.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (child.Process.HasExited)
        {
            await Task.WhenAll(child.CreatorThreadLifetime, child.StandardOutputDrain, child.StandardErrorDrain)
                .ConfigureAwait(false);
        }
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
        var executableName = Path.GetFileNameWithoutExtension(executable);
        if (string.Equals(executableName, "testhost", StringComparison.OrdinalIgnoreCase))
            executable = "dotnet";
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(typeof(HorseIdentityMigrationSupervisor).Assembly.Location);
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
