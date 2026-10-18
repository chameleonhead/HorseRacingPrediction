using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

namespace HorseRacingPrediction.Api.IdentityMigration;

/// <summary>Owns per-source supervisor locks and the durable maintenance markers distinct from runtime locks.</summary>
internal static class HorseIdentityMigrationMaintenanceOwnership
{
    private const string ControlLockPrefix = "HorseIdentityMigrationSupervisorControl:v1:";
    private const int MaximumRecordBytes = 1024;

    internal static HorseIdentityMigrationSupervisorControlLease AcquireSupervisorControlLease(
        IEnumerable<string> sourcePaths)
    {
        var paths = HorseIdentityMigrationRuntimeOwnership.CanonicalizeSourcePaths(sourcePaths);
        ValidateSidecarPaths(paths);
        var locks = new List<FileStream>(paths.Length);
        try
        {
            foreach (var path in paths)
            {
                HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(path, sourceFile: true);
                var parent = Path.GetDirectoryName(path)
                    ?? throw new IOException("A configured source path has no parent directory.");
                Directory.CreateDirectory(parent);
                HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(path, sourceFile: true);

                var lockPath = GetControlLockPath(path);
                HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(lockPath, sourceFile: true);
                var (handle, created) = OpenControlLock(lockPath);
                try
                {
                    ValidateControlLock(handle, path, created);
                    locks.Add(handle);
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }

            return new(paths, locks);
        }
        catch
        {
            for (var index = locks.Count - 1; index >= 0; index--) locks[index].Dispose();
            throw;
        }
    }

    internal static string GetControlLockPath(string sourcePath) => GetSiblingPath(sourcePath,
        ".horse-identity-migration.supervisor-control.lock");

    internal static string GetControlOwnerRecordPath(string sourcePath) => GetSiblingPath(sourcePath,
        ".horse-identity-migration.supervisor-control.owner");

    internal static string GetMaintenanceGatePath(string sourcePath) => GetSiblingPath(sourcePath,
        ".horse-identity-migration.maintenance");

    private static void ValidateSidecarPaths(string[] paths)
    {
        var comparer = HorseIdentityMigrationConfiguredSources.PathComparer;
        var sidecars = paths.Concat(paths.SelectMany(path => new[]
        {
            HorseIdentityMigrationRuntimeOwnership.GetRuntimeOwnershipGatePath(path),
            GetControlLockPath(path), GetControlOwnerRecordPath(path), GetMaintenanceGatePath(path),
        })).ToArray();
        if (sidecars.Distinct(comparer).Count() != sidecars.Length)
            throw new IOException("Configured sources and maintenance sidecars cannot alias one another.");
    }

    internal static bool ValidateParentAuthorizationForSources(IEnumerable<string> sourcePaths,
        string sourceSetHash, int parentProcessId, long parentStartTicks, string nonceHash, string purpose,
        Guid? operationId, string? requestHash)
    {
        if (!HorseIdentityMigrationMaintenanceCapability.IsHash(sourceSetHash)
            || !HorseIdentityMigrationMaintenanceCapability.IsHash(nonceHash)
            || parentProcessId <= 0 || parentStartTicks <= 0
            || (purpose == "api-bootstrap" && (operationId is not null || requestHash is not null))
            || (purpose != "api-bootstrap" && (operationId is null || requestHash is null))
            || purpose is not ("api-bootstrap" or "cli-operation" or "readonly-reverify")
            || (requestHash is not null && !HorseIdentityMigrationMaintenanceCapability.IsHash(requestHash)))
            return false;

        var paths = HorseIdentityMigrationRuntimeOwnership.CanonicalizeSourcePaths(sourcePaths);
        if (!string.Equals(HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(paths), sourceSetHash,
                StringComparison.Ordinal) || !IsSameLiveProcess(parentProcessId, parentStartTicks)) return false;

        foreach (var path in paths)
        {
            if (!IsControlLockHeld(GetControlLockPath(path))) return false;
            var record = ReadOwnerRecord(path);
            if (record is null || record.Purpose != purpose
                || record.OperationId != operationId || record.RequestHash != requestHash
                || record.SourceSetHash != sourceSetHash || record.ParentProcessId != parentProcessId
                || record.ParentStartTicks != parentStartTicks || !FixedHashEquals(record.NonceHash, nonceHash))
                return false;
        }
        return paths.Length > 0;
    }

    internal static bool ValidateChildAuthorizationForSources(IEnumerable<string> sourcePaths,
        string sourceSetHash, int parentProcessId, long parentStartTicks, string nonceHash, string purpose,
        Guid? operationId, string? requestHash)
    {
        if (!IsActualParent(parentProcessId, parentStartTicks)
            || !ValidateParentAuthorizationForSources(sourcePaths, sourceSetHash, parentProcessId,
                parentStartTicks, nonceHash, purpose, operationId, requestHash)) return false;
        return IsActualParent(parentProcessId, parentStartTicks);
    }

    private static bool IsActualParent(int expectedProcessId, long expectedStartTicks)
    {
        try
        {
            var actualParentProcessId = OperatingSystem.IsWindows()
                ? GetWindowsParentProcessId(Environment.ProcessId)
                : GetUnixParentProcessId();
            return actualParentProcessId == expectedProcessId
                && IsSameLiveProcess(expectedProcessId, expectedStartTicks);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or PlatformNotSupportedException or IOException)
        {
            return false;
        }
    }

    private static int GetWindowsParentProcessId(int processId)
    {
        const uint snapshotProcesses = 0x00000002;
        using var snapshot = new SafeSnapshotHandle(CreateToolhelp32Snapshot(snapshotProcesses, 0));
        if (snapshot.IsInvalid) throw new IOException("Could not inspect the process parent identity.");
        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32First(snapshot, ref entry)) throw new IOException("Could not inspect the process parent identity.");
        do
        {
            if (entry.ProcessId == (uint)processId) return checked((int)entry.ParentProcessId);
        }
        while (Process32Next(snapshot, ref entry));
        throw new IOException("The current process is absent from the process snapshot.");
    }

    private static int GetUnixParentProcessId()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The current platform does not expose a supported parent-process query.");
        return GetParentProcessIdUnix();
    }

    internal static long GetProcessStartTicks(int processId)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (OperatingSystem.IsLinux())
        {
            // Process.StartTime is not stable to the tick across independent Linux queries.
            // /proc/<pid>/stat field 22 is the kernel's PID-reuse-safe start time in clock ticks.
            var stat = File.ReadAllText($"/proc/{processId}/stat");
            var closingCommandDelimiter = stat.LastIndexOf(')');
            if (closingCommandDelimiter < 0 || closingCommandDelimiter + 1 >= stat.Length)
                throw new IOException("Could not inspect the process start identity.");
            var fields = stat[(closingCommandDelimiter + 1)..].TrimStart()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            const int startTimeFieldIndex = 19; // field 22; the first post-command field is field 3.
            if (fields.Length <= startTimeFieldIndex
                || !long.TryParse(fields[startTimeFieldIndex], NumberStyles.None, CultureInfo.InvariantCulture,
                    out var startTicks) || startTicks <= 0)
                throw new IOException("Could not inspect the process start identity.");
            return startTicks;
        }

        using var process = Process.GetProcessById(processId);
        if (process.HasExited) throw new IOException("The process has exited.");
        return process.StartTime.ToUniversalTime().Ticks;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeSnapshotHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeSnapshotHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int GetParentProcessIdUnix();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        internal uint Size;
        internal uint Usage;
        internal uint ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId;
        internal uint Threads;
        internal uint ParentProcessId;
        internal int BasePriority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ExecutableFile;
    }

    private sealed class SafeSnapshotHandle(IntPtr handle) : SafeHandle(handle, ownsHandle: true)
    {
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    internal static bool ValidateMaintenanceGateSet(IEnumerable<string> sourcePaths, Guid operationId,
        string requestHash, string sourceSetHash)
    {
        var paths = HorseIdentityMigrationRuntimeOwnership.CanonicalizeSourcePaths(sourcePaths);
        if (paths.Length == 0 || operationId == Guid.Empty || !HorseIdentityMigrationMaintenanceCapability.IsHash(requestHash)
            || !string.Equals(HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(paths), sourceSetHash,
                StringComparison.Ordinal)) return false;

        foreach (var path in paths)
        {
            var record = ReadMaintenanceGate(path);
            if (record is null || record.OperationId != operationId || record.RequestHash != requestHash
                || record.SourceSetHash != sourceSetHash || record.CreatorProcessId <= 0
                || record.CreatorStartTicks <= 0 || !HorseIdentityMigrationMaintenanceCapability.IsHash(record.CreatorNonceHash))
                return false;
        }
        return true;
    }

    internal static async Task InstallMaintenanceGatesAsync(
        HorseIdentityMigrationRuntimeOwnership runtimeOwnership,
        HorseIdentityMigrationMaintenanceCapability.ApiBootstrapAuthorization authorization,
        Guid operationId,
        string immutableRequestHash,
        HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken = default)
        => await InstallMaintenanceGatesCoreAsync(runtimeOwnership, authorization, operationId,
            immutableRequestHash, journal, HorseIdentityMigrationExecutionPhase.StoppingApi, cancellationToken)
            .ConfigureAwait(false);

    internal static async Task InstallStandaloneMaintenanceGatesAsync(
        HorseIdentityMigrationRuntimeOwnership runtimeOwnership,
        HorseIdentityMigrationSupervisorControlLease controlLease,
        Guid operationId,
        string immutableRequestHash,
        HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeOwnership);
        ArgumentNullException.ThrowIfNull(controlLease);
        runtimeOwnership.ThrowIfDisposed();
        if (!controlLease.OwnsSameSourceSet(runtimeOwnership.SourcePaths))
            throw new IOException("Standalone gate installation requires matching live runtime and control leases.");
        var authorization = controlLease.IssueApiBootstrapAuthorization();
        await InstallMaintenanceGatesCoreAsync(runtimeOwnership, authorization, operationId, immutableRequestHash,
            journal, HorseIdentityMigrationExecutionPhase.Preparing, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InstallMaintenanceGatesCoreAsync(
        HorseIdentityMigrationRuntimeOwnership runtimeOwnership,
        HorseIdentityMigrationMaintenanceCapability.ApiBootstrapAuthorization authorization,
        Guid operationId,
        string immutableRequestHash,
        HorseIdentityMigrationJournal journal,
        HorseIdentityMigrationExecutionPhase requiredPhase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtimeOwnership);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(journal);
        if (operationId == Guid.Empty || !HorseIdentityMigrationMaintenanceCapability.IsHash(immutableRequestHash))
            throw new ArgumentException("The maintenance operation identity is invalid.");

        runtimeOwnership.ThrowIfDisposed();
        var paths = runtimeOwnership.SourcePaths;
        var sourceSetHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(paths);
        if (!authorization.ValidateForSourcePaths(paths))
            throw new IOException("The API bootstrap authorization does not match the held source ownership.");

        var intent = await journal.GetExecutionIntentAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (intent is null || intent.ImmutableRequestHash != immutableRequestHash
            || intent.Phase != requiredPhase)
            throw new InvalidOperationException("A matching saved execution intent at the required pre-write phase is required to install maintenance gates.");

        var creator = HorseIdentityMigrationMaintenanceCapability.CurrentProcessIdentity();
        var expected = EncodeRecord([
            "v1", operationId.ToString("D"), immutableRequestHash, sourceSetHash,
            creator.ProcessId.ToString(CultureInfo.InvariantCulture),
            creator.StartTicks.ToString(CultureInfo.InvariantCulture), authorization.GetNonceHash()]);
        var created = new List<(string Path, byte[] Bytes)>();
        var openHandles = new List<FileStream>();
        try
        {
            foreach (var sourcePath in paths)
            {
                var markerPath = GetMaintenanceGatePath(sourcePath);
                HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(markerPath, sourceFile: true);
                try
                {
                    var handle = new FileStream(markerPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                        1024, FileOptions.WriteThrough);
                    openHandles.Add(handle);
                    var bytes = Encoding.ASCII.GetBytes(expected);
                    handle.Write(bytes);
                    handle.Flush(flushToDisk: true);
                    created.Add((markerPath, bytes));
                }
                catch (IOException) when (File.Exists(markerPath))
                {
                    var existing = ReadBoundedRecord(markerPath);
                    if (existing is null || !FixedBytesEquals(existing, Encoding.ASCII.GetBytes(expected)))
                        throw new InvalidDataException("An existing maintenance gate is corrupt or belongs to another operation.");
                }
            }
        }
        catch
        {
            foreach (var handle in openHandles) handle.Dispose();
            for (var index = created.Count - 1; index >= 0; index--)
                DeleteOnlyMatchingCreatedGate(created[index].Path, created[index].Bytes);
            throw;
        }
        finally
        {
            foreach (var handle in openHandles) handle.Dispose();
        }
    }

    internal static Task ClearMaintenanceGatesAsync(
        HorseIdentityMigrationRuntimeOwnership runtimeOwnership,
        HorseIdentityMigrationSupervisorControlLease controlLease,
        HorseIdentityMigrationMaintenanceCapability.OperationAuthorization authorization,
        HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeOwnership);
        ArgumentNullException.ThrowIfNull(controlLease);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(journal);
        runtimeOwnership.ThrowIfDisposed();
        var paths = runtimeOwnership.SourcePaths;
        if (!controlLease.OwnsSameSourceSet(paths) || !authorization.ValidateForSourcePaths(paths))
            throw new IOException("Maintenance gate clearing requires matching live source, control, and operation ownership.");
        return ClearMaintenanceGatesCoreAsync(paths, runtimeOwnership, authorization.OperationId,
            authorization.RequestHash, authorization.SourceSetHash, journal, cancellationToken);
    }

    internal static Task ClearMaintenanceGatesAfterReadonlyReverificationAsync(
        HorseIdentityMigrationRuntimeOwnership runtimeOwnership,
        HorseIdentityMigrationSupervisorControlLease controlLease,
        HorseIdentityMigrationMaintenanceCapability.ReadonlyReverificationAuthorization authorization,
        HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeOwnership);
        ArgumentNullException.ThrowIfNull(controlLease);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(journal);
        runtimeOwnership.ThrowIfDisposed();
        var paths = runtimeOwnership.SourcePaths;
        if (!controlLease.OwnsSameSourceSet(paths) || !authorization.ValidateForSourcePaths(paths))
            throw new IOException("Read-only re-verification gate clearing requires matching live source, control, and re-verification ownership.");
        return ClearMaintenanceGatesCoreAsync(paths, runtimeOwnership, authorization.OperationId,
            authorization.RequestHash, authorization.SourceSetHash, journal, cancellationToken);
    }

    private static async Task ClearMaintenanceGatesCoreAsync(string[] paths,
        HorseIdentityMigrationRuntimeOwnership runtimeOwnership, Guid operationId, string requestHash,
        string sourceSetHash, HorseIdentityMigrationJournal journal, CancellationToken cancellationToken)
    {

        var status = await journal.GetStatusAsync(operationId, cancellationToken).ConfigureAwait(false);
        var intent = await journal.GetExecutionIntentAsync(operationId, cancellationToken)
            .ConfigureAwait(false);
        if (status?.Status != HorseIdentityMigrationOperationStatus.Succeeded
            || intent is null || intent.ImmutableRequestHash != requestHash
            || intent.Phase != HorseIdentityMigrationExecutionPhase.Verified)
            throw new InvalidOperationException("Only a matching succeeded and verified operation may clear maintenance gates.");

        foreach (var path in paths)
        {
            var record = ReadMaintenanceGate(path);
            if (record is null || record.OperationId != operationId
                || record.RequestHash != requestHash || record.SourceSetHash != sourceSetHash)
                throw new InvalidDataException("The maintenance gate set is incomplete or belongs to another operation.");
        }

        runtimeOwnership.ThrowIfDisposed();
        foreach (var path in paths)
        {
            var markerPath = GetMaintenanceGatePath(path);
            File.Delete(markerPath);
        }
    }

    private static (FileStream Handle, bool Created) OpenControlLock(string path)
    {
        try
        {
            return (new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                512, FileOptions.WriteThrough), true);
        }
        catch (IOException) when (File.Exists(path))
        {
            return (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
                512, FileOptions.WriteThrough), false);
        }
    }

    private static void ValidateControlLock(FileStream handle, string sourcePath, bool created)
    {
        var expected = Encoding.ASCII.GetBytes(ControlLockPrefix +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath))) + "\n");
        if (created)
        {
            handle.Write(expected);
            handle.Flush(flushToDisk: true);
            return;
        }
        if (handle.Length != expected.Length) throw new InvalidDataException("A supervisor control lock marker is corrupt.");
        handle.Position = 0;
        var actual = new byte[expected.Length];
        handle.ReadExactly(actual);
        if (!FixedBytesEquals(actual, expected)) throw new InvalidDataException("A supervisor control lock marker is corrupt.");
    }

    private static bool IsControlLockHeld(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool IsSameLiveProcess(int processId, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && GetProcessStartTicks(processId) == startTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void WriteOwnerRecords(IEnumerable<string> sourcePaths, string record)
    {
        var bytes = Encoding.ASCII.GetBytes(record);
        foreach (var path in sourcePaths)
        {
            var marker = GetControlOwnerRecordPath(path);
            HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(marker, sourceFile: true);
            using var handle = new FileStream(marker, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                MaximumRecordBytes, FileOptions.WriteThrough);
            if (handle.Length > MaximumRecordBytes) throw new InvalidDataException("A supervisor owner record is oversized.");
            if (handle.Length > 0 && ReadRecord(handle) is null)
                throw new InvalidDataException("A supervisor owner record is corrupt.");
            handle.SetLength(0);
            handle.Position = 0;
            handle.Write(bytes);
            handle.Flush(flushToDisk: true);
        }
    }

    private static OwnerRecord? ReadOwnerRecord(string sourcePath)
    {
        var path = GetControlOwnerRecordPath(sourcePath);
        if (!File.Exists(path)) return null;
        HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(path, sourceFile: true);
        using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadRecord(handle);
    }

    private static OwnerRecord? ReadRecord(FileStream handle)
    {
        if (handle.Length is <= 0 or > MaximumRecordBytes) return null;
        var bytes = new byte[(int)handle.Length];
        handle.Position = 0;
        handle.ReadExactly(bytes);
        var fields = Encoding.ASCII.GetString(bytes).Split('\n');
        if (fields.Length != 8 || fields[0] != "v1"
            || fields[1] is not ("api-bootstrap" or "cli-operation" or "readonly-reverify")
            || !HorseIdentityMigrationMaintenanceCapability.IsHash(fields[4])
            || !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0
            || !long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var startTicks) || startTicks <= 0
            || !HorseIdentityMigrationMaintenanceCapability.IsHash(fields[7])) return null;
        Guid? operationId = null;
        string? requestHash = null;
        if (fields[1] == "api-bootstrap")
        {
            if (fields[2] != "-" || fields[3] != "-") return null;
        }
        else
        {
            if (!Guid.TryParseExact(fields[2], "D", out var parsed) || parsed == Guid.Empty
                || !HorseIdentityMigrationMaintenanceCapability.IsHash(fields[3])) return null;
            operationId = parsed;
            requestHash = fields[3];
        }
        return new(fields[1], operationId, requestHash, fields[4], pid, startTicks, fields[7]);
    }

    private static MaintenanceRecord? ReadMaintenanceGate(string sourcePath)
    {
        var path = GetMaintenanceGatePath(sourcePath);
        var bytes = ReadBoundedRecord(path);
        if (bytes is null) return null;
        var fields = Encoding.ASCII.GetString(bytes).Split('\n');
        if (fields.Length != 7 || fields[0] != "v1" || !Guid.TryParseExact(fields[1], "D", out var operationId)
            || operationId == Guid.Empty || !HorseIdentityMigrationMaintenanceCapability.IsHash(fields[2])
            || !HorseIdentityMigrationMaintenanceCapability.IsHash(fields[3])
            || !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var creatorPid) || creatorPid <= 0
            || !long.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var creatorStart) || creatorStart <= 0
            || !HorseIdentityMigrationMaintenanceCapability.IsHash(fields[6])) return null;
        return new(operationId, fields[2], fields[3], creatorPid, creatorStart, fields[6]);
    }

    private static byte[]? ReadBoundedRecord(string path)
    {
        if (!File.Exists(path)) return null;
        HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(path, sourceFile: true);
        using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (handle.Length is <= 0 or > MaximumRecordBytes) return null;
        var bytes = new byte[(int)handle.Length];
        handle.ReadExactly(bytes);
        return bytes;
    }

    private static void DeleteOnlyMatchingCreatedGate(string path, byte[] expected)
    {
        try
        {
            if (ReadBoundedRecord(path) is { } actual && FixedBytesEquals(actual, expected)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string EncodeOwnerRecord(string purpose, Guid? operationId, string? requestHash,
        string sourceSetHash, int parentPid, long parentStart, string nonceHash) => EncodeRecord([
        "v1", purpose, operationId?.ToString("D") ?? "-", requestHash ?? "-", sourceSetHash,
        parentPid.ToString(CultureInfo.InvariantCulture), parentStart.ToString(CultureInfo.InvariantCulture), nonceHash]);

    private static string EncodeRecord(string[] fields) => string.Join('\n', fields);
    private static string GetSiblingPath(string sourcePath, string suffix) => Path.Combine(
        Path.GetDirectoryName(sourcePath) ?? throw new IOException("A configured source path has no parent directory."),
        $".{Path.GetFileName(sourcePath)}{suffix}");
    private static bool FixedBytesEquals(byte[] first, byte[] second) => first.Length == second.Length
        && CryptographicOperations.FixedTimeEquals(first, second);
    private static bool FixedHashEquals(string first, string second) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));

    private sealed record OwnerRecord(string Purpose, Guid? OperationId, string? RequestHash,
        string SourceSetHash, int ParentProcessId, long ParentStartTicks, string NonceHash);
    private sealed record MaintenanceRecord(Guid OperationId, string RequestHash, string SourceSetHash,
        int CreatorProcessId, long CreatorStartTicks, string CreatorNonceHash);

    internal sealed class HorseIdentityMigrationSupervisorControlLease : IDisposable
    {
        private readonly string[] _sourcePaths;
        private readonly List<FileStream> _handles;
        private bool _disposed;

        internal HorseIdentityMigrationSupervisorControlLease(string[] sourcePaths, List<FileStream> handles)
        {
            _sourcePaths = sourcePaths;
            _handles = handles;
        }

        internal HorseIdentityMigrationMaintenanceCapability.ApiBootstrapAuthorization IssueApiBootstrapAuthorization()
        {
            ThrowIfDisposed();
            var identity = HorseIdentityMigrationMaintenanceCapability.CurrentProcessIdentity();
            var sourceHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(_sourcePaths);
            var nonce = HorseIdentityMigrationMaintenanceCapability.CreateNonce();
            var authorization = HorseIdentityMigrationMaintenanceCapability.CreateApiBootstrapAuthorization(
                sourceHash, identity.ProcessId, identity.StartTicks, nonce);
            WriteOwnerRecords(_sourcePaths, EncodeOwnerRecord("api-bootstrap", null, null, sourceHash,
                identity.ProcessId, identity.StartTicks, authorization.GetNonceHash()));
            CryptographicOperations.ZeroMemory(nonce);
            return authorization;
        }

        internal bool OwnsSameSourceSet(IEnumerable<string> sourcePaths)
        {
            ThrowIfDisposed();
            var expected = HorseIdentityMigrationRuntimeOwnership.CanonicalizeSourcePaths(sourcePaths);
            return expected.SequenceEqual(_sourcePaths, HorseIdentityMigrationConfiguredSources.PathComparer);
        }

        internal async Task<HorseIdentityMigrationMaintenanceCapability.OperationAuthorization> IssueOperationAuthorizationAsync(
            Guid operationId, string immutableRequestHash, HorseIdentityMigrationJournal journal,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(journal);
            if (operationId == Guid.Empty || !HorseIdentityMigrationMaintenanceCapability.IsHash(immutableRequestHash))
                throw new ArgumentException("The migration operation identity is invalid.");
            var intent = await journal.GetExecutionIntentAsync(operationId, cancellationToken).ConfigureAwait(false);
            if (intent is null || intent.ImmutableRequestHash != immutableRequestHash
                || intent.Phase != HorseIdentityMigrationExecutionPhase.Applying)
                throw new InvalidOperationException("A matching applying execution intent is required before issuing CLI authorization.");
            if (!ValidateMaintenanceGateSet(_sourcePaths, operationId, immutableRequestHash,
                    HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(_sourcePaths)))
                throw new InvalidOperationException("A complete matching maintenance gate set is required before issuing CLI authorization.");

            var identity = HorseIdentityMigrationMaintenanceCapability.CurrentProcessIdentity();
            var sourceHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(_sourcePaths);
            var nonce = HorseIdentityMigrationMaintenanceCapability.CreateNonce();
            var authorization = HorseIdentityMigrationMaintenanceCapability.CreateOperationAuthorization(
                operationId, immutableRequestHash, sourceHash, identity.ProcessId, identity.StartTicks, nonce);
            WriteOwnerRecords(_sourcePaths, EncodeOwnerRecord("cli-operation", operationId, immutableRequestHash,
                sourceHash, identity.ProcessId, identity.StartTicks, authorization.GetNonceHash()));
            CryptographicOperations.ZeroMemory(nonce);
            return authorization;
        }

        internal async Task<HorseIdentityMigrationMaintenanceCapability.ReadonlyReverificationAuthorization?>
            IssueReadonlyReverificationAuthorizationAsync(Guid operationId, string immutableRequestHash,
                HorseIdentityMigrationJournal journal, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(journal);
            var status = await journal.GetStatusAsync(operationId, cancellationToken).ConfigureAwait(false);
            var intent = await journal.GetExecutionIntentAsync(operationId, cancellationToken).ConfigureAwait(false);
            if (status?.Status != HorseIdentityMigrationOperationStatus.Succeeded
                || intent?.ImmutableRequestHash != immutableRequestHash
                || intent.Phase is not (HorseIdentityMigrationExecutionPhase.DataSucceeded
                    or HorseIdentityMigrationExecutionPhase.Verifying)) return null;

            var sourceHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(_sourcePaths);
            if (!ValidateMaintenanceGateSet(_sourcePaths, operationId, immutableRequestHash, sourceHash)) return null;
            var identity = HorseIdentityMigrationMaintenanceCapability.CurrentProcessIdentity();
            var nonce = HorseIdentityMigrationMaintenanceCapability.CreateNonce();
            var authorization = new HorseIdentityMigrationMaintenanceCapability.ReadonlyReverificationAuthorization(
                operationId, immutableRequestHash, sourceHash, identity.ProcessId, identity.StartTicks, nonce);
            WriteOwnerRecords(_sourcePaths, EncodeOwnerRecord("readonly-reverify", operationId, immutableRequestHash,
                sourceHash, identity.ProcessId, identity.StartTicks, authorization.GetNonceHash()));
            CryptographicOperations.ZeroMemory(nonce);
            return authorization;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HorseIdentityMigrationSupervisorControlLease));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var index = _handles.Count - 1; index >= 0; index--) _handles[index].Dispose();
        }
    }
}
