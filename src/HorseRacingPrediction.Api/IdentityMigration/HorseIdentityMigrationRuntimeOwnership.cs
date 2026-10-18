using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HorseRacingPrediction.Api.IdentityMigration;

/// <summary>Holds exclusive, cross-process reservations for configured migration source files.</summary>
internal sealed class HorseIdentityMigrationRuntimeOwnership : IDisposable
{
    private const string GatePrefix = "HorseIdentityMigrationSourceOwnership:v1:";
    private readonly string[] _sourcePaths;
    private readonly List<FileStream> _handles;
    private bool _disposed;

    private HorseIdentityMigrationRuntimeOwnership(string[] sourcePaths, List<FileStream> handles)
    {
        _sourcePaths = sourcePaths;
        _handles = handles;
    }

    internal static HorseIdentityMigrationRuntimeOwnership Acquire(IEnumerable<string> sourcePaths) =>
        AcquireCore(sourcePaths, operationAuthorization: null);

    internal static HorseIdentityMigrationRuntimeOwnership AcquireForOperation(
        IEnumerable<string> sourcePaths,
        HorseIdentityMigrationMaintenanceCapability.OperationAuthorization operationAuthorization)
    {
        ArgumentNullException.ThrowIfNull(operationAuthorization);
        return AcquireCore(sourcePaths, operationAuthorization, readonlyAuthorization: null);
    }

    internal static async Task<HorseIdentityMigrationRuntimeOwnership> AcquireForReadonlyReverificationAsync(
        IEnumerable<string> sourcePaths,
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease,
        HorseIdentityMigrationMaintenanceCapability.ReadonlyReverificationAuthorization authorization,
        HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(controlLease);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(journal);
        var paths = CanonicalizeSourcePaths(sourcePaths);
        if (!controlLease.OwnsSameSourceSet(paths))
            throw new IOException("Read-only re-verification requires the matching live supervisor control lease.");
        if (!await authorization.ValidateForSourcePathsAsync(paths, journal, cancellationToken).ConfigureAwait(false))
            throw new IOException("The read-only re-verification authorization is not eligible for this source set.");

        var ownership = AcquireCore(paths, operationAuthorization: null, readonlyAuthorization: authorization);
        if (!await authorization.ValidateForSourcePathsAsync(paths, journal, cancellationToken).ConfigureAwait(false))
        {
            ownership.Dispose();
            throw new IOException("The read-only re-verification authorization changed while source ownership was acquired.");
        }
        return ownership;
    }

    internal static HorseIdentityMigrationRuntimeOwnership AcquireForMaintenanceGateManagement(
        IEnumerable<string> sourcePaths,
        HorseIdentityMigrationMaintenanceOwnership.HorseIdentityMigrationSupervisorControlLease controlLease,
        HorseIdentityMigrationMaintenanceCapability.OperationAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(controlLease);
        ArgumentNullException.ThrowIfNull(authorization);
        var paths = CanonicalizeSourcePaths(sourcePaths);
        if (!controlLease.OwnsSameSourceSet(paths))
            throw new IOException("Maintenance gate management requires the matching live supervisor control lease.");
        return AcquireCore(paths, authorization, readonlyAuthorization: null);
    }

    private static HorseIdentityMigrationRuntimeOwnership AcquireCore(IEnumerable<string> sourcePaths,
        HorseIdentityMigrationMaintenanceCapability.OperationAuthorization? operationAuthorization,
        HorseIdentityMigrationMaintenanceCapability.ReadonlyReverificationAuthorization? readonlyAuthorization = null)
    {
        var paths = CanonicalizeSourcePaths(sourcePaths);
        var comparer = HorseIdentityMigrationConfiguredSources.PathComparer;
        if (paths.Length == 0)
        {
            if (operationAuthorization is not null || readonlyAuthorization is not null)
                throw new IOException("An operation authorization cannot be used without configured source files.");
            return new([], []);
        }

        var gates = paths.Select(GetGatePath).ToArray();
        var allPaths = paths.Concat(gates).ToArray();
        if (allPaths.Distinct(comparer).Count() != allPaths.Length)
            throw new IOException("A configured source path collides with a source ownership gate.");

        var handles = new List<FileStream>(paths.Length);
        try
        {
            foreach (var (sourcePath, gatePath) in paths.Zip(gates))
            {
                ValidateExistingAncestors(sourcePath, sourceFile: true);
                var parent = Path.GetDirectoryName(sourcePath)
                    ?? throw new IOException("The configured source path has no parent directory.");
                Directory.CreateDirectory(parent);
                ValidateExistingAncestors(sourcePath, sourceFile: true);

                ValidateExistingAncestors(gatePath, sourceFile: true);
                var (handle, created) = OpenGate(gatePath);
                try
                {
                    ValidateRegularFile(handle, gatePath);
                    InitializeOrValidateGate(handle, sourcePath, created);
                    handles.Add(handle);
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }

            if (operationAuthorization is null)
            {
                if (readonlyAuthorization is null
                    && paths.Any(path => File.Exists(HorseIdentityMigrationMaintenanceOwnership.GetMaintenanceGatePath(path))))
                    throw new IOException("A configured source is held by an active migration maintenance gate.");
            }
            else if (!operationAuthorization.ValidateForSourcePaths(paths) || readonlyAuthorization is not null)
            {
                throw new IOException("The operation authorization does not match the complete maintenance gate set.");
            }
            else if (!HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(paths,
                         operationAuthorization.OperationId, operationAuthorization.RequestHash,
                         operationAuthorization.SourceSetHash))
                throw new IOException("The operation authorization does not match the complete maintenance gate set.");
            if (readonlyAuthorization is not null
                && (!readonlyAuthorization.ValidateForSourcePaths(paths)
                    || !HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(paths,
                        readonlyAuthorization.OperationId, readonlyAuthorization.RequestHash,
                        readonlyAuthorization.SourceSetHash)))
                throw new IOException("The read-only re-verification authorization does not match the complete maintenance gate set.");

            return new(paths, handles);
        }
        catch
        {
            for (var index = handles.Count - 1; index >= 0; index--) handles[index].Dispose();
            throw;
        }
    }

    internal string[] SourcePaths => _sourcePaths.ToArray();

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal static string[] CanonicalizeSourcePaths(IEnumerable<string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var comparer = HorseIdentityMigrationConfiguredSources.PathComparer;
        return sourcePaths.Select(path => Path.GetFullPath(path)).Select(CanonicalizeWindowsSourcePath)
            .Distinct(comparer).OrderBy(path => path, comparer).ToArray();
    }

    internal static string ComputeSourceSetHash(IEnumerable<string> sourcePaths)
    {
        var canonical = CanonicalizeSourcePaths(sourcePaths);
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', canonical));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    internal static string GetRuntimeOwnershipGatePath(string sourcePath) => GetGatePath(sourcePath);

    private static string GetGatePath(string sourcePath)
    {
        var parent = Path.GetDirectoryName(sourcePath)
            ?? throw new IOException("The configured source path has no parent directory.");
        var name = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(name)) throw new IOException("The configured source path has no file name.");
        return Path.Combine(parent, $".{name}.horse-identity-migration.lock");
    }

    private static string CanonicalizeWindowsSourcePath(string sourcePath)
    {
        if (!OperatingSystem.IsWindows()) return sourcePath;

        var parent = Path.GetDirectoryName(sourcePath)
            ?? throw new IOException("The configured source path has no parent directory.");
        ValidateExistingAncestors(sourcePath, sourceFile: true);
        Directory.CreateDirectory(parent);
        ValidateExistingAncestors(sourcePath, sourceFile: true);

        string canonicalPath;
        if (File.Exists(sourcePath))
        {
            using var sourceHandle = OpenWindowsPathHandle(sourcePath, directory: false);
            canonicalPath = GetFinalWindowsPath(sourceHandle);
        }
        else
        {
            using var parentHandle = OpenWindowsPathHandle(parent, directory: true);
            canonicalPath = Path.Combine(GetFinalWindowsPath(parentHandle), Path.GetFileName(sourcePath));
        }

        // NTFS path lookup is case-insensitive. Normalize both the sidecar name and the marker
        // key so differently-cased callers share the same reservation and can validate it.
        return canonicalPath.ToUpperInvariant();
    }

    private static SafeFileHandle OpenWindowsPathHandle(string path, bool directory)
    {
        const uint ShareReadWriteDelete = 0x00000007;
        const uint OpenExisting = 3;
        const uint BackupSemantics = 0x02000000;
        var handle = CreateFileWindows(path, 0, ShareReadWriteDelete, IntPtr.Zero, OpenExisting,
            directory ? BackupSemantics : 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("Could not canonicalize the configured Windows source path.",
                new Win32Exception(error));
        }
        return handle;
    }

    private static string GetFinalWindowsPath(SafeFileHandle handle)
    {
        var length = GetFinalPathNameByHandleWindows(handle, null, 0, 0);
        if (length == 0) throw new IOException("Could not resolve the configured source's final Windows path.",
            new Win32Exception(Marshal.GetLastWin32Error()));
        var buffer = new StringBuilder(checked((int)length + 1));
        var written = GetFinalPathNameByHandleWindows(handle, buffer, (uint)buffer.Capacity, 0);
        if (written == 0 || written >= buffer.Capacity)
            throw new IOException("Could not resolve the configured source's final Windows path.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        return buffer.ToString();
    }

    private static (FileStream Handle, bool Created) OpenGate(string gatePath)
    {
        try
        {
            return (new FileStream(gatePath, FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough), true);
        }
        catch (IOException) when (File.Exists(gatePath) || Directory.Exists(gatePath))
        {
            // Open-only disposition is authoritative: an existing gate must always be validated,
            // including when another process creates it between our CreateNew and open attempt.
            return (new FileStream(gatePath, FileMode.Open, FileAccess.ReadWrite,
                FileShare.None, 4096, FileOptions.WriteThrough), false);
        }
    }

    private static void InitializeOrValidateGate(FileStream handle, string sourcePath, bool created)
    {
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)));
        var expected = Encoding.ASCII.GetBytes(GatePrefix + pathHash + "\n");
        if (created)
        {
            handle.SetLength(0);
            handle.Write(expected);
            handle.Flush(flushToDisk: true);
            return;
        }

        if (handle.Length != expected.Length)
            throw new InvalidDataException("A source ownership gate is corrupt or belongs to a different path.");
        handle.Position = 0;
        var actual = new byte[expected.Length];
        handle.ReadExactly(actual);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new InvalidDataException("A source ownership gate is corrupt or belongs to a different path.");
    }

    internal static void ValidateExistingAncestors(string path, bool sourceFile)
    {
        var fullPath = Path.GetFullPath(path);
        var current = fullPath;
        var first = true;
        while (!string.IsNullOrEmpty(current))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                current = Path.GetDirectoryName(current)!;
                first = false;
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                current = Path.GetDirectoryName(current)!;
                first = false;
                continue;
            }

            var linkTarget = new FileInfo(current).LinkTarget ?? new DirectoryInfo(current).LinkTarget;
            if ((attributes & FileAttributes.ReparsePoint) != 0 || linkTarget is not null)
                throw new IOException("Configured source paths cannot traverse symbolic links or reparse points.");
            if (first && sourceFile && (attributes & FileAttributes.Directory) != 0)
                throw new IOException("A configured source path must identify a regular file, not a directory.");
            if (first && sourceFile) ValidateRegularPath(fullPath);

            first = false;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || comparerEquals(parent, current)) break;
            current = parent;
        }

        static bool comparerEquals(string left, string right) =>
            HorseIdentityMigrationConfiguredSources.PathComparer.Equals(left, right);
    }

    private static void ValidateRegularPath(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var information = GetWindowsInformation(handle);
            if ((information.FileAttributes & (uint)FileAttributes.Directory) != 0 || information.NumberOfLinks != 1)
                throw new IOException("Configured source files must be regular files without hard-link aliases.");
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            if (statx(AtCurrentWorkingDirectory, path, 0, StatxBasicStats, out var information) != 0)
                throw new IOException("Could not inspect the configured source file identity.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            if ((information.Mode & LinuxFileTypeMask) != LinuxRegularFileType || information.LinkCount != 1)
                throw new IOException("Configured source files must be regular files without hard-link aliases.");
            return;
        }

        throw new PlatformNotSupportedException("Source identity checks are implemented for Windows and Linux.");
    }

    private static void ValidateRegularFile(FileStream handle, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var information = GetWindowsInformation(handle.SafeFileHandle);
            if ((information.FileAttributes & (uint)FileAttributes.Directory) != 0 || information.NumberOfLinks != 1)
                throw new IOException($"The source ownership gate for '{path}' is not a regular unaliased file.");
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            if (statx(AtCurrentWorkingDirectory, path, 0, StatxBasicStats, out var information) != 0
                || (information.Mode & LinuxFileTypeMask) != LinuxRegularFileType || information.LinkCount != 1)
                throw new IOException($"The source ownership gate for '{path}' is not a regular unaliased file.");
            return;
        }

        throw new PlatformNotSupportedException("Source identity checks are implemented for Windows and Linux.");
    }

    private static WindowsFileInformation GetWindowsInformation(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
            throw new IOException("Could not inspect the configured file identity.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        return information;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (var index = _handles.Count - 1; index >= 0; index--) _handles[index].Dispose();
    }

    private const int AtCurrentWorkingDirectory = -100;
    private const uint StatxBasicStats = 0x7ff;
    private const ushort LinuxFileTypeMask = 0xF000;
    private const ushort LinuxRegularFileType = 0x8000;

    [DllImport("libc", SetLastError = true, EntryPoint = "statx")]
    private static extern int statx(int directoryFileDescriptor, string path, int flags, uint mask, out LinuxStatx information);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsFileInformation information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFileWindows(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandleWindows(SafeFileHandle file, StringBuilder? filePath,
        uint filePathLength, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStatx
    {
        internal uint Mask;
        internal uint BlockSize;
        internal ulong Attributes;
        internal uint LinkCount;
        internal uint UserId;
        internal uint GroupId;
        internal ushort Mode;
        private ushort _spare0;
        internal ulong Inode;
        internal ulong Size;
        internal ulong Blocks;
        internal ulong AttributesMask;
        private long _accessedSeconds;
        private uint _accessedNanoseconds;
        private int _spare1;
        private long _createdSeconds;
        private uint _createdNanoseconds;
        private int _spare2;
        private long _changedSeconds;
        private uint _changedNanoseconds;
        private int _spare3;
        private long _modifiedSeconds;
        private uint _modifiedNanoseconds;
        private int _spare4;
        internal uint DeviceMajor;
        internal uint DeviceMinor;
        internal uint RDeviceMajor;
        internal uint RDeviceMinor;
        internal ulong MountId;
        internal uint DirectIoMemoryAlignment;
        internal uint DirectIoOffsetAlignment;
        private ulong _spare5;
        private ulong _spare6;
        private ulong _spare7;
        private ulong _spare8;
        private ulong _spare9;
        private ulong _spare10;
        private ulong _spare11;
        private ulong _spare12;
        private ulong _spare13;
        private ulong _spare14;
        private ulong _spare15;
        private ulong _spare16;
    }
}
