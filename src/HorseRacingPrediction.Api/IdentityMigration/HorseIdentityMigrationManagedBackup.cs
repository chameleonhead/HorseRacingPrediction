using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.IdentityMigration;

/// <summary>
/// Creates an immutable, bounded backup package for a single offline migration operation.
/// Callers must already have stopped writers and acquired source ownership.
/// </summary>
internal sealed class HorseIdentityMigrationManagedBackup
{
    internal const int MaximumMembers = 32;
    internal const long MaximumPayloadBytes = 16L * 1024 * 1024 * 1024;
    internal const int MaximumMetadataBytes = 1024 * 1024;
    private const long FreeSpaceReserveBytes = 64L * 1024 * 1024;
    private const string ReceiptFileName = "receipt.json";
    private readonly string _stateDirectory;
    private readonly Func<string, long> _availableBytes;
    private readonly Action<string>? _afterMemberCopied;

    internal HorseIdentityMigrationManagedBackup(
        string trustedCollectionStateDirectory,
        Func<string, long>? availableBytes = null,
        Action<string>? afterMemberCopied = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedCollectionStateDirectory);
        _stateDirectory = Path.GetFullPath(trustedCollectionStateDirectory);
        _availableBytes = availableBytes ?? GetAvailableBytes;
        _afterMemberCopied = afterMemberCopied;
    }

    internal static HorseIdentityMigrationManagedBackup ForJournal(
        HorseIdentityMigrationJournal journal,
        Func<string, long>? availableBytes = null,
        Action<string>? afterMemberCopied = null)
    {
        ArgumentNullException.ThrowIfNull(journal);
        return new(Path.GetDirectoryName(journal.DatabasePath)
            ?? throw new IOException("The migration journal has no state directory."), availableBytes, afterMemberCopied);
    }

    internal async Task<HorseIdentityMigrationBackupReceipt> CreateAsync(
        Guid operationId,
        HorseIdentityMigrationConfiguredSources sources,
        HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(journal);
        if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operationId));
        EnsureTrustedStateDirectory(journal);

        var packageDirectory = GetPackageDirectory(operationId);
        if (Directory.Exists(packageDirectory) || File.Exists(packageDirectory))
            throw new IOException("A managed backup package already exists and will not be overwritten.");

        var inventory = BuildInventory(sources, journal);
        ValidateInventory(inventory);
        ValidateSourceAndDestinationDisjoint(inventory, packageDirectory);
        inventory = await HashInventoryAsync(inventory, cancellationToken).ConfigureAwait(false);
        ValidateNoExistingTargetAliases(packageDirectory);
        foreach (var member in inventory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(member.SourcePath, sourceFile: true);
            if (member.IsSqliteDatabase) await ValidateSqliteIntegrityAsync(member.SourcePath, cancellationToken).ConfigureAwait(false);
        }

        var payloadBytes = inventory.Sum(member => member.Length);
        EnsureSpace(packageDirectory, checked(2 * payloadBytes + FreeSpaceReserveBytes));

        Directory.CreateDirectory(packageDirectory);
        try
        {
            foreach (var member in inventory)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = GetTargetPath(packageDirectory, member.LogicalName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyAndVerifyAsync(member, target, cancellationToken).ConfigureAwait(false);
                _afterMemberCopied?.Invoke(member.LogicalName);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var after = BuildInventory(sources, journal);
            ValidateInventory(after);
            after = await HashInventoryAsync(after, cancellationToken).ConfigureAwait(false);
            if (!InventoriesEqual(inventory, after))
                throw new IOException("Configured backup sources changed during capture.");

            foreach (var member in after.Where(member => member.IsSqliteDatabase))
                await ValidateSqliteIntegrityAsync(member.SourcePath, cancellationToken).ConfigureAwait(false);
            foreach (var member in inventory.Where(member => member.IsSqliteDatabase))
                await ValidateSqliteIntegrityAsync(GetTargetPath(packageDirectory, member.LogicalName), cancellationToken)
                    .ConfigureAwait(false);

            EnsureSpace(packageDirectory, checked(payloadBytes + FreeSpaceReserveBytes));
            var receipt = CreateReceipt(operationId, inventory, sources, journal);
            cancellationToken.ThrowIfCancellationRequested();
            var receiptBytes = SerializeBounded(receipt);
            var receiptPath = Path.Combine(packageDirectory, ReceiptFileName);
            var pendingReceiptPath = Path.Combine(packageDirectory, "receipt.pending");
            await using (var stream = new FileStream(pendingReceiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(receiptBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            EnsureSpace(packageDirectory, checked(payloadBytes + FreeSpaceReserveBytes));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(pendingReceiptPath, receiptPath);

            return await ValidateAsync(operationId, sources, journal, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Incomplete packages are intentionally preserved for diagnosis and are never trusted without a receipt.
            throw;
        }
    }

    internal async Task<HorseIdentityMigrationBackupReceipt> ValidateAsync(
        Guid operationId,
        HorseIdentityMigrationConfiguredSources sources,
        HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(journal);
        if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operationId));
        EnsureTrustedStateDirectory(journal);
        var packageDirectory = GetPackageDirectory(operationId);
        ValidateNoExistingTargetAliases(packageDirectory);
        var receiptPath = Path.Combine(packageDirectory, ReceiptFileName);
        HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(receiptPath, sourceFile: true);
        if (!File.Exists(receiptPath)) throw new InvalidDataException("Managed backup receipt is missing.");
        var info = new FileInfo(receiptPath);
        if (info.Length <= 0 || info.Length > MaximumMetadataBytes)
            throw new InvalidDataException("Managed backup receipt exceeds its metadata bound.");
        var receipt = JsonSerializer.Deserialize<HorseIdentityMigrationBackupReceipt>(
            await File.ReadAllBytesAsync(receiptPath, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidDataException("Managed backup receipt is invalid.");
        ValidateReceiptShape(receipt, operationId);

        if (!string.Equals(receipt.SourceSetHash, ComputeSourceSetHash(sources, journal), StringComparison.Ordinal))
            throw new InvalidDataException("Managed backup receipt source set differs from configured stores.");
        ValidateReceiptMembers(receipt, sources);
        var expectedMembers = receipt.Members.OrderBy(member => member.LogicalName, StringComparer.Ordinal).ToArray();
        if (!string.Equals(receipt.ManifestHash, ComputeManifestHash(receipt.OperationId, receipt.SourceSetHash,
            expectedMembers), StringComparison.Ordinal))
            throw new InvalidDataException("Managed backup receipt manifest digest is invalid.");

        var (packageMembers, packageDirectories) = EnumerateBoundedPackage(packageDirectory);
        var expectedNames = expectedMembers.Select(member => member.LogicalName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expectedDirectories = expectedNames.SelectMany(name =>
                name.Contains('/') ? new[] { name[..name.LastIndexOf('/')] } : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!packageMembers.SequenceEqual(expectedNames.Append(ReceiptFileName).OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal)
            || !packageDirectories.SequenceEqual(expectedDirectories, StringComparer.Ordinal))
            throw new InvalidDataException("Managed backup package membership differs from its receipt.");

        foreach (var member in expectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = GetTargetPath(packageDirectory, member.LogicalName);
            HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(path, sourceFile: true);
            var targetInfo = new FileInfo(path);
            if (targetInfo.Length != member.Length || !string.Equals(await HashFileAsync(path, member.Length, cancellationToken).ConfigureAwait(false),
                    member.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Managed backup member does not match its receipt.");
            if (member.IsSqliteDatabase) await ValidateSqliteIntegrityAsync(path, cancellationToken).ConfigureAwait(false);
            if (member.LogicalName == "ancillary/migration-journal-initialized.marker")
            {
                var marker = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                if (!marker.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(HorseIdentityMigrationJournal.InitializationMarkerContent)))
                    throw new InvalidDataException("Managed backup journal marker is invalid.");
            }
        }
        return receipt;
    }

    internal static void ValidateInventoryBounds(IReadOnlyList<HorseIdentityMigrationBackupMember> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (inventory.Count == 0 || inventory.Count > MaximumMembers)
            throw new InvalidDataException("Managed backup member count is outside its bound.");
        var total = 0L;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in inventory)
        {
            if (member is null || member.Length < 0 || !IsSafeLogicalName(member.LogicalName)
                || !names.Add(member.LogicalName))
                throw new InvalidDataException("Managed backup inventory contains an invalid member.");
            total = checked(total + member.Length);
            if (total > MaximumPayloadBytes) throw new InvalidDataException("Managed backup payload exceeds its bound.");
        }
        var probeMembers = inventory.Select(member => member with { Sha256 = new string('A', 64) }).ToArray();
        var probe = new HorseIdentityMigrationBackupReceipt(1, Guid.NewGuid(), new string('A', 64), probeMembers,
            new string('A', 64));
        if (JsonSerializer.SerializeToUtf8Bytes(probe).Length > MaximumMetadataBytes)
            throw new InvalidDataException("Managed backup metadata exceeds its bound.");
    }

    private List<SourceMember> BuildInventory(HorseIdentityMigrationConfiguredSources sources,
        HorseIdentityMigrationJournal journal)
    {
        if (sources.EventStorePath is null)
            throw new InvalidDataException("A physical event store is required for migration backup.");
        var members = new List<SourceMember>();
        AddDatabase("application/event-store.db", sources.EventStorePath, required: true, application: true);
        AddDatabase("application/collection-platform.db", sources.CollectionPlatformPath, required: true, application: true);
        AddDatabase("application/prediction-schedule.db", sources.PredictionSchedulePath, required: true, application: true);
        AddDatabase("application/local-collection-queue.db", sources.LocalCollectionQueuePath,
            required: sources.LocalCollectionQueuePath is not null, application: true);
        AddDatabase("ancillary/migration-journal.db", journal.DatabasePath, required: true, application: false);
        AddFile("ancillary/migration-journal-initialized.marker", journal.InitializationMarkerPath,
            required: true, application: false, sqlite: false);
        return members;

        void AddDatabase(string logicalName, string? databasePath, bool required, bool application)
        {
            if (databasePath is null)
            {
                if (required) throw new InvalidDataException($"Required configured store is missing: {logicalName}.");
                return;
            }
            var fullPath = Path.GetFullPath(databasePath);
            if (!File.Exists(fullPath))
            {
                if (required) throw new FileNotFoundException($"Required configured store is missing: {logicalName}.", fullPath);
                return;
            }
            AddFile(logicalName, fullPath, required: true, application, sqlite: true);
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            {
                var sidecar = fullPath + suffix;
                if (!File.Exists(sidecar)) continue;
                if (suffix is "-wal" or "-journal" && new FileInfo(sidecar).Length > 0)
                    throw new InvalidDataException($"Nonempty SQLite {suffix} refuses coherent backup: {logicalName}.");
                AddFile(logicalName + suffix, sidecar, required: false, application, sqlite: false);
            }
        }

        void AddFile(string logicalName, string path, bool required, bool application, bool sqlite)
        {
            if (!File.Exists(path))
            {
                if (required) throw new FileNotFoundException($"Required backup member is missing: {logicalName}.", path);
                return;
            }
            HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(path, sourceFile: true);
            var file = new FileInfo(path);
            if (file.Length > MaximumPayloadBytes) throw new InvalidDataException("A managed backup member exceeds the payload bound.");
            if (!application && logicalName == "ancillary/migration-journal.db" && file.Length > HorseIdentityMigrationJournal.MaximumJournalBytes)
                throw new InvalidDataException("Migration journal exceeds its existing size bound.");
            if (logicalName.EndsWith("initialized.marker", StringComparison.Ordinal))
            {
                if (file.Length > 64) throw new InvalidDataException("Migration journal marker exceeds its bound.");
                var marker = File.ReadAllBytes(path);
                if (!marker.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(HorseIdentityMigrationJournal.InitializationMarkerContent)))
                    throw new InvalidDataException("Migration journal marker is invalid.");
            }
            members.Add(new(logicalName, path, file.Length, string.Empty, sqlite, application));
        }
    }

    private void ValidateInventory(List<SourceMember> inventory)
    {
        var projected = inventory.Select(member => new HorseIdentityMigrationBackupMember(member.LogicalName,
            member.Length, new string('0', 64), member.IsSqliteDatabase, member.IsApplicationMember)).ToArray();
        ValidateInventoryBounds(projected);
        var journalPackageBytes = inventory.Where(member => member.LogicalName.StartsWith("ancillary/", StringComparison.Ordinal))
            .Sum(member => member.Length);
        if (journalPackageBytes > HorseIdentityMigrationJournal.MaximumJournalBytes)
            throw new InvalidDataException("Migration journal package exceeds its existing size bound.");
        var metadataProbe = new HorseIdentityMigrationBackupReceipt(1, Guid.NewGuid(), new string('0', 64),
            projected, new string('0', 64));
        if (JsonSerializer.SerializeToUtf8Bytes(metadataProbe).Length > MaximumMetadataBytes)
            throw new InvalidDataException("Managed backup metadata exceeds its bound.");
    }

    private async Task CopyAndVerifyAsync(SourceMember member, string target, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(member.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != member.Length) throw new IOException("A backup source changed before copy.");
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long copied = 0;
        while (copied < member.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, member.Length - copied);
            var count = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new IOException("A backup source ended before its captured length.");
            sourceHash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            copied = checked(copied + count);
        }
        if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
            throw new IOException("A backup source grew beyond its captured length.");
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
        await output.DisposeAsync().ConfigureAwait(false);
        var sourceDigest = sourceHash.GetHashAndReset();
        if (copied != member.Length || !string.Equals(Convert.ToHexString(sourceDigest), member.Sha256, StringComparison.Ordinal))
            throw new IOException("Managed backup copy hash verification failed.");
        var destinationHash = await HashFileAsync(target, member.Length, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(destinationHash, member.Sha256, StringComparison.Ordinal))
            throw new IOException("Managed backup destination readback hash verification failed.");
    }

    private static async Task ValidateSqliteIntegrityAsync(string path, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = $"{new Uri(Path.GetFullPath(path)).AbsoluteUri}?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 2,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture), "ok", StringComparison.Ordinal))
            throw new InvalidDataException($"SQLite integrity check failed for '{Path.GetFileName(path)}'.");
    }

    private static async Task<string> HashFileAsync(string path, long expectedLength, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != expectedLength || expectedLength < 0 || expectedLength > MaximumPayloadBytes)
            throw new IOException("A managed backup member changed length during hashing.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long readTotal = 0;
        while (readTotal < expectedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, expectedLength - readTotal);
            var count = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new IOException("A managed backup member ended during hashing.");
            hash.AppendData(buffer, 0, count);
            readTotal += count;
        }
        if (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
            throw new IOException("A managed backup member grew during hashing.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private HorseIdentityMigrationBackupReceipt CreateReceipt(Guid operationId, IReadOnlyList<SourceMember> inventory,
        HorseIdentityMigrationConfiguredSources sources, HorseIdentityMigrationJournal journal)
    {
        var ordered = inventory.OrderBy(member => member.LogicalName, StringComparer.Ordinal)
            .Select(member => new HorseIdentityMigrationBackupMember(member.LogicalName, member.Length,
                member.Sha256, member.IsSqliteDatabase, member.IsApplicationMember)).ToArray();
        var sourceSetHash = ComputeSourceSetHash(sources, journal);
        var digest = ComputeManifestHash(operationId, sourceSetHash, ordered);
        return new(1, operationId, sourceSetHash, ordered, digest);
    }

    private static string ComputeSourceSetHash(HorseIdentityMigrationConfiguredSources sources,
        HorseIdentityMigrationJournal journal)
    {
        var identities = new (string Name, string? Path)[]
        {
            ("application/event-store.db", sources.EventStorePath),
            ("application/collection-platform.db", sources.CollectionPlatformPath),
            ("application/prediction-schedule.db", sources.PredictionSchedulePath),
            ("application/local-collection-queue.db", sources.LocalCollectionQueuePath),
            ("ancillary/migration-journal.db", journal.DatabasePath),
            ("ancillary/migration-journal-initialized.marker", journal.InitializationMarkerPath),
        };
        var canonical = string.Join("\n", identities.OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => $"{member.Name}|{(member.Path is null ? "absent" : Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(member.Path)))))}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ComputeManifestHash(Guid operationId, string sourceSetHash,
        IEnumerable<HorseIdentityMigrationBackupMember> members)
    {
        var canonical = new StringBuilder($"v1\n{operationId:D}\n{sourceSetHash}\n");
        foreach (var member in members.OrderBy(member => member.LogicalName, StringComparer.Ordinal))
            canonical.Append(member.LogicalName).Append('|').Append(member.Length).Append('|').Append(member.Sha256)
                .Append('|').Append(member.IsSqliteDatabase ? '1' : '0').Append('|')
                .Append(member.IsApplicationMember ? 'A' : 'N').Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool InventoriesEqual(IReadOnlyList<SourceMember> left, IReadOnlyList<SourceMember> right) =>
        left.Count == right.Count && left.OrderBy(member => member.LogicalName, StringComparer.Ordinal)
            .Zip(right.OrderBy(member => member.LogicalName, StringComparer.Ordinal))
            .All(pair => pair.First.LogicalName == pair.Second.LogicalName
                && pair.First.Length == pair.Second.Length
                && pair.First.IsSqliteDatabase == pair.Second.IsSqliteDatabase
                && pair.First.IsApplicationMember == pair.Second.IsApplicationMember
                && string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.Ordinal));

    private static void ValidateSourceAndDestinationDisjoint(IReadOnlyList<SourceMember> inventory, string packageDirectory)
    {
        var sourcePaths = inventory.Select(member => Path.GetFullPath(member.SourcePath)).ToArray();
        if (sourcePaths.Distinct(HorseIdentityMigrationConfiguredSources.PathComparer).Count() != sourcePaths.Length)
            throw new InvalidDataException("Configured backup members cannot alias one another.");
        foreach (var member in inventory)
        {
            var target = GetTargetPath(packageDirectory, member.LogicalName);
            if (sourcePaths.Contains(target, HorseIdentityMigrationConfiguredSources.PathComparer))
                throw new InvalidDataException("Managed backup destination aliases a configured source.");
        }
    }

    private static async Task<List<SourceMember>> HashInventoryAsync(List<SourceMember> inventory,
        CancellationToken cancellationToken)
    {
        var result = new List<SourceMember>(inventory.Count);
        foreach (var member in inventory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = await HashFileAsync(member.SourcePath, member.Length, cancellationToken).ConfigureAwait(false);
            result.Add(member with { Sha256 = hash });
        }
        return result;
    }

    private byte[] SerializeBounded(HorseIdentityMigrationBackupReceipt receipt)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt);
        if (bytes.Length > MaximumMetadataBytes) throw new InvalidDataException("Managed backup metadata exceeds its bound.");
        return bytes;
    }

    private static void ValidateReceiptShape(HorseIdentityMigrationBackupReceipt receipt, Guid operationId)
    {
        if (receipt.Version != 1 || receipt.OperationId != operationId || receipt.Members is null
            || !IsSha256(receipt.SourceSetHash) || !IsSha256(receipt.ManifestHash))
            throw new InvalidDataException("Managed backup receipt identity is invalid.");
        ValidateInventoryBounds(receipt.Members);
        if (receipt.Members.Any(member => !IsSha256(member.Sha256)))
            throw new InvalidDataException("Managed backup receipt contains an invalid member hash.");
    }

    internal static void ValidateReceiptMembers(HorseIdentityMigrationBackupReceipt receipt,
        HorseIdentityMigrationConfiguredSources sources)
    {
        if (sources.EventStorePath is null || sources.CollectionPlatformPath is null || sources.PredictionSchedulePath is null)
            throw new InvalidDataException("Required configured stores are missing from the backup identity.");
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            "application/event-store.db",
            "application/collection-platform.db",
            "application/prediction-schedule.db",
            "ancillary/migration-journal.db",
            "ancillary/migration-journal-initialized.marker",
        };
        if (sources.LocalCollectionQueuePath is not null) required.Add("application/local-collection-queue.db");
        var names = receipt.Members.Select(member => member.LogicalName).ToHashSet(StringComparer.Ordinal);
        if (!required.IsSubsetOf(names)) throw new InvalidDataException("A required configured backup member is missing.");
        var allowedDatabases = new HashSet<string>(StringComparer.Ordinal)
        {
            "application/event-store.db", "application/collection-platform.db", "application/prediction-schedule.db",
            "ancillary/migration-journal.db",
        };
        if (sources.LocalCollectionQueuePath is not null) allowedDatabases.Add("application/local-collection-queue.db");
        foreach (var member in receipt.Members)
        {
            var isDatabase = allowedDatabases.Contains(member.LogicalName);
            var sidecarBase = allowedDatabases.FirstOrDefault(database =>
                member.LogicalName.StartsWith(database, StringComparison.Ordinal)
                && new[] { "-wal", "-shm", "-journal" }.Any(suffix => member.LogicalName == database + suffix));
            var isSidecar = sidecarBase is not null;
            var isMarker = member.LogicalName == "ancillary/migration-journal-initialized.marker";
            if ((!isDatabase && !isSidecar && !isMarker)
                || member.IsSqliteDatabase != isDatabase
                || member.IsApplicationMember != member.LogicalName.StartsWith("application/", StringComparison.Ordinal)
                || (member.LogicalName.EndsWith("-wal", StringComparison.Ordinal)
                    || member.LogicalName.EndsWith("-journal", StringComparison.Ordinal)) && member.Length != 0
                || isMarker && member.Length > 64)
                throw new InvalidDataException("Managed backup receipt contains a member outside the configured schema.");
        }
        var ancillaryBytes = receipt.Members.Where(member => member.LogicalName.StartsWith("ancillary/", StringComparison.Ordinal))
            .Sum(member => member.Length);
        if (ancillaryBytes > HorseIdentityMigrationJournal.MaximumJournalBytes)
            throw new InvalidDataException("Managed backup journal package exceeds its existing size bound.");
    }

    private static (string[] Files, string[] Directories) EnumerateBoundedPackage(string root)
    {
        HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(root, sourceFile: false);
        if (!Directory.Exists(root)) throw new InvalidDataException("Managed backup package directory is missing.");
        var files = new List<string>();
        var directories = new List<string>();
        var visited = 0;
        Visit(root);
        return (files.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            directories.OrderBy(value => value, StringComparer.Ordinal).ToArray());

        void Visit(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++visited > MaximumMembers + 8)
                    throw new InvalidDataException("Managed backup package contains too many entries.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Managed backup package cannot contain links or reparse points.");
                var relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(entry, sourceFile: false);
                    directories.Add(relative);
                    Visit(entry);
                }
                else
                {
                    HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(entry, sourceFile: true);
                    files.Add(relative);
                }
            }
        }
    }

    private string GetPackageDirectory(Guid operationId) => Path.Combine(_stateDirectory, "backups",
        $"horse-id-migration-{operationId:N}");

    private static string GetTargetPath(string root, string logicalName)
    {
        if (!IsSafeLogicalName(logicalName)) throw new InvalidDataException("Managed backup member path is invalid.");
        var path = Path.GetFullPath(Path.Combine(root, logicalName.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Managed backup member escaped its package directory.");
        return path;
    }

    private static bool IsSafeLogicalName(string name) => !string.IsNullOrWhiteSpace(name)
        && !name.StartsWith("/", StringComparison.Ordinal) && !name.Contains('\\')
        && name.Split('/').All(part => part.Length > 0 && part is not "." and not "..")
        && !name.Equals(ReceiptFileName, StringComparison.Ordinal);

    private static bool IsSha256(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit)
        && string.Equals(value, value.ToUpperInvariant(), StringComparison.Ordinal);

    private void EnsureTrustedStateDirectory(HorseIdentityMigrationJournal journal)
    {
        var journalDirectory = Path.GetDirectoryName(Path.GetFullPath(journal.DatabasePath));
        if (journalDirectory is null || !HorseIdentityMigrationConfiguredSources.PathComparer.Equals(
                _stateDirectory, journalDirectory))
            throw new InvalidOperationException("Managed backup root must be the configured migration Collection StateDirectory.");
    }

    private void EnsureSpace(string path, long required)
    {
        var probePath = Path.GetFullPath(path);
        while (!Directory.Exists(probePath))
        {
            probePath = Path.GetDirectoryName(probePath)
                ?? throw new IOException("Could not find an existing destination ancestor for space admission.");
        }
        var available = _availableBytes(probePath);
        if (available < required) throw new IOException("Insufficient free space for managed backup admission.");
    }

    private static long GetAvailableBytes(string path)
    {
        return new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace;
    }

    private static void ValidateNoExistingTargetAliases(string path)
    {
        HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(path, sourceFile: false);
    }

    private sealed record SourceMember(string LogicalName, string SourcePath, long Length, string Sha256,
        bool IsSqliteDatabase, bool IsApplicationMember);
}

internal sealed record HorseIdentityMigrationBackupMember(
    string LogicalName,
    long Length,
    string Sha256,
    bool IsSqliteDatabase,
    bool IsApplicationMember);

internal sealed record HorseIdentityMigrationBackupReceipt(
    int Version,
    Guid OperationId,
    string SourceSetHash,
    IReadOnlyList<HorseIdentityMigrationBackupMember> Members,
    string ManifestHash);
