using HorseRacingPrediction.Api.IdentityMigration;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationManagedBackupTests
{
    [TestMethod]
    public async Task CreateProducesBoundedExactPackageAndReadonlyValidationPreservesSourceFiles()
    {
        using var fixture = new BackupFixture();
        var before = fixture.SnapshotSources();
        var backup = new HorseIdentityMigrationManagedBackup(fixture.StateDirectory, _ => long.MaxValue);

        var receipt = await backup.CreateAsync(fixture.OperationId, fixture.Sources, fixture.Journal);
        var verified = await backup.ValidateAsync(fixture.OperationId, fixture.Sources, fixture.Journal);

        Assert.AreEqual(fixture.OperationId, receipt.OperationId);
        Assert.AreEqual(receipt.ManifestHash, verified.ManifestHash);
        Assert.AreEqual(5, receipt.Members.Count);
        Assert.IsTrue(receipt.Members.Any(member => member.LogicalName == "application/event-store.db"));
        Assert.IsTrue(receipt.Members.Any(member => member.LogicalName == "application/collection-platform.db"));
        Assert.IsTrue(receipt.Members.Any(member => member.LogicalName == "application/prediction-schedule.db"));
        Assert.IsTrue(receipt.Members.Any(member => member.LogicalName == "ancillary/migration-journal-initialized.marker"));
        Assert.AreEqual(6, Directory.EnumerateFiles(fixture.PackagePath, "*", SearchOption.AllDirectories).Count());
        foreach (var member in receipt.Members)
        {
            var source = fixture.SourceFor(member.LogicalName);
            var target = Path.Combine(fixture.PackagePath, member.LogicalName.Replace('/', Path.DirectorySeparatorChar));
            CollectionAssert.AreEqual(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(target));
        }
        Assert.AreEqual("event", await fixture.ReadSingleValueAsync(Path.Combine(fixture.PackagePath, "application", "event-store.db")));

        CollectionAssert.AreEqual(before, fixture.SnapshotSources());
    }

    [TestMethod]
    public async Task PresentEmptySidecarsAreCopiedButNonemptyWalAndRollbackJournalAreRejected()
    {
        using var fixture = new BackupFixture();
        await File.WriteAllBytesAsync(fixture.EventPath + "-shm", []);
        var backup = new HorseIdentityMigrationManagedBackup(fixture.StateDirectory, _ => long.MaxValue);

        var receipt = await backup.CreateAsync(fixture.OperationId, fixture.Sources, fixture.Journal);
        Assert.IsTrue(receipt.Members.Any(member => member.LogicalName == "application/event-store.db-shm"));

        using var walFixture = new BackupFixture();
        await File.WriteAllBytesAsync(walFixture.EventPath + "-wal", [1]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new HorseIdentityMigrationManagedBackup(walFixture.StateDirectory, _ => long.MaxValue)
                .CreateAsync(walFixture.OperationId, walFixture.Sources, walFixture.Journal));

        using var journalFixture = new BackupFixture();
        await File.WriteAllBytesAsync(journalFixture.EventPath + "-journal", [1]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new HorseIdentityMigrationManagedBackup(journalFixture.StateDirectory, _ => long.MaxValue)
                .CreateAsync(journalFixture.OperationId, journalFixture.Sources, journalFixture.Journal));
    }

    [TestMethod]
    public async Task ReadonlyIntegrityOnWalModeDatabaseLeavesAbsentCompanionsAbsent()
    {
        using var fixture = new BackupFixture();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.EventPath,
            Pooling = false,
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            Assert.AreEqual("wal", Convert.ToString(await command.ExecuteScalarAsync()));
        }
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(fixture.EventPath + suffix)) File.Delete(fixture.EventPath + suffix);
        var before = fixture.SnapshotSources();

        await new HorseIdentityMigrationManagedBackup(fixture.StateDirectory, _ => long.MaxValue)
            .CreateAsync(fixture.OperationId, fixture.Sources, fixture.Journal);

        CollectionAssert.AreEqual(before, fixture.SnapshotSources());
    }

    [TestMethod]
    public async Task ExistingPackageExtraMemberAndTamperedMemberFailClosedWithoutCleanup()
    {
        using var fixture = new BackupFixture();
        var backup = new HorseIdentityMigrationManagedBackup(fixture.StateDirectory, _ => long.MaxValue);
        await backup.CreateAsync(fixture.OperationId, fixture.Sources, fixture.Journal);
        var eventBackup = Path.Combine(fixture.PackagePath, "application", "event-store.db");
        var original = await File.ReadAllBytesAsync(eventBackup);
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            backup.CreateAsync(fixture.OperationId, fixture.Sources, fixture.Journal));
        File.Delete(fixture.EventPath);
        await backup.ValidateAsync(fixture.OperationId, fixture.Sources, fixture.Journal);
        await File.WriteAllBytesAsync(fixture.EventPath, original);
        await File.WriteAllBytesAsync(fixture.EventPath + "-shm", []);
        await backup.ValidateAsync(fixture.OperationId, fixture.Sources, fixture.Journal);
        File.Delete(fixture.EventPath + "-shm");
        await File.WriteAllTextAsync(Path.Combine(fixture.PackagePath, "unexpected.txt"), "extra");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            backup.ValidateAsync(fixture.OperationId, fixture.Sources, fixture.Journal));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.PackagePath, "unexpected.txt")), "Untrusted packages are not auto-cleaned.");
        File.Delete(Path.Combine(fixture.PackagePath, "unexpected.txt"));
        Directory.CreateDirectory(Path.Combine(fixture.PackagePath, "unexpected-empty"));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            backup.ValidateAsync(fixture.OperationId, fixture.Sources, fixture.Journal));
        Directory.Delete(Path.Combine(fixture.PackagePath, "unexpected-empty"));
        await File.WriteAllBytesAsync(eventBackup, [0]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            backup.ValidateAsync(fixture.OperationId, fixture.Sources, fixture.Journal));
        await File.WriteAllBytesAsync(eventBackup, original);
        Assert.IsTrue((await backup.ValidateAsync(fixture.OperationId, fixture.Sources, fixture.Journal)).OperationId
            == fixture.OperationId);
    }

    [TestMethod]
    public async Task LowSpaceMutationAndCancellationPreserveUnusablePartialPackages()
    {
        using var lowSpace = new BackupFixture();
        var insufficient = new HorseIdentityMigrationManagedBackup(lowSpace.StateDirectory, _ => 1);
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            insufficient.CreateAsync(lowSpace.OperationId, lowSpace.Sources, lowSpace.Journal));
        Assert.IsFalse(Directory.Exists(lowSpace.PackagePath), "Admission must happen before package creation.");

        using var mutation = new BackupFixture();
        var mutatingBackup = new HorseIdentityMigrationManagedBackup(mutation.StateDirectory, _ => long.MaxValue,
            logicalName =>
            {
                if (logicalName == "application/event-store.db") File.AppendAllText(mutation.EventPath, "mutation");
            });
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            mutatingBackup.CreateAsync(mutation.OperationId, mutation.Sources, mutation.Journal));
        Assert.IsTrue(Directory.Exists(mutation.PackagePath), "Interrupted/mismatched capture remains for diagnosis.");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            mutatingBackup.ValidateAsync(mutation.OperationId, mutation.Sources, mutation.Journal));

        using var cancelled = new BackupFixture();
        using var source = new CancellationTokenSource();
        var cancellingBackup = new HorseIdentityMigrationManagedBackup(cancelled.StateDirectory, _ => long.MaxValue,
            _ => source.Cancel());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cancellingBackup.CreateAsync(
            cancelled.OperationId, cancelled.Sources, cancelled.Journal, source.Token));
        Assert.IsTrue(Directory.Exists(cancelled.PackagePath));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => cancellingBackup.ValidateAsync(
            cancelled.OperationId, cancelled.Sources, cancelled.Journal));

        using var afterSpace = new BackupFixture();
        var spaceChecks = 0;
        var postCopyNoSpace = new HorseIdentityMigrationManagedBackup(afterSpace.StateDirectory,
            _ => ++spaceChecks == 1 ? long.MaxValue : 0);
        await Assert.ThrowsExactlyAsync<IOException>(() => postCopyNoSpace.CreateAsync(
            afterSpace.OperationId, afterSpace.Sources, afterSpace.Journal));
        Assert.IsTrue(Directory.Exists(afterSpace.PackagePath));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => postCopyNoSpace.ValidateAsync(
            afterSpace.OperationId, afterSpace.Sources, afterSpace.Journal));
    }

    [TestMethod]
    public void InventoryBoundsRejectMemberCountPayloadAndMetadataWithoutLargeAllocations()
    {
        var thirtyThree = Enumerable.Range(0, HorseIdentityMigrationManagedBackup.MaximumMembers + 1)
            .Select(index => new HorseIdentityMigrationBackupMember($"db/{index}.db", 0, new string('A', 64), true, true))
            .ToArray();
        Assert.ThrowsExactly<InvalidDataException>(() => HorseIdentityMigrationManagedBackup.ValidateInventoryBounds(thirtyThree));
        Assert.ThrowsExactly<InvalidDataException>(() => HorseIdentityMigrationManagedBackup.ValidateInventoryBounds(
            [new("app/a.db", HorseIdentityMigrationManagedBackup.MaximumPayloadBytes + 1, new string('A', 64), true, true)]));
        Assert.ThrowsExactly<InvalidDataException>(() => HorseIdentityMigrationManagedBackup.ValidateInventoryBounds(
            [new("../escape", 0, new string('A', 64), true, true)]));
        Assert.ThrowsExactly<InvalidDataException>(() => HorseIdentityMigrationManagedBackup.ValidateInventoryBounds(
            [new($"app/{new string('x', HorseIdentityMigrationManagedBackup.MaximumMetadataBytes + 1)}", 0,
                new string('A', 64), true, true)]));
    }

    [TestMethod]
    public void ReceiptSchemaRequiresEveryConfiguredDatabaseAndCorrectMemberClassification()
    {
        using var fixture = new BackupFixture();
        var receipt = new HorseIdentityMigrationBackupReceipt(1, fixture.OperationId, new string('A', 64),
            [
                new("application/event-store.db", 1, new string('A', 64), true, true),
                new("application/collection-platform.db", 1, new string('A', 64), true, true),
                new("application/prediction-schedule.db", 1, new string('A', 64), true, true),
                new("ancillary/migration-journal.db", 1, new string('A', 64), true, false),
                new("ancillary/migration-journal-initialized.marker", 37, new string('A', 64), false, false),
            ], new string('A', 64));
        HorseIdentityMigrationManagedBackup.ValidateReceiptMembers(receipt, fixture.Sources);

        Assert.ThrowsExactly<InvalidDataException>(() => HorseIdentityMigrationManagedBackup.ValidateReceiptMembers(
            receipt with { Members = receipt.Members.Where(member => member.LogicalName != "application/event-store.db").ToArray() },
            fixture.Sources));
        Assert.ThrowsExactly<InvalidDataException>(() => HorseIdentityMigrationManagedBackup.ValidateReceiptMembers(
            receipt with
            {
                Members = receipt.Members.Select(member => member.LogicalName == "application/event-store.db"
                ? member with { IsSqliteDatabase = false } : member).ToArray()
            }, fixture.Sources));

        var queueSources = fixture.Sources with { LocalCollectionQueuePath = Path.Combine(fixture.Root, "queue.db") };
        Assert.ThrowsExactly<InvalidDataException>(() => HorseIdentityMigrationManagedBackup.ValidateReceiptMembers(receipt, queueSources));
    }

    [TestMethod]
    public async Task ConfiguredLocalQueueMustExistAndDestinationSpaceUsesItsActualMount()
    {
        using (var fixture = new BackupFixture())
        {
            var queuePath = Path.Combine(fixture.Root, "queue.db");
            var queueSources = fixture.Sources with { LocalCollectionQueuePath = queuePath };
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
                new HorseIdentityMigrationManagedBackup(fixture.StateDirectory, _ => long.MaxValue)
                    .CreateAsync(fixture.OperationId, queueSources, fixture.Journal));
        }

        if (!OperatingSystem.IsLinux() || !Directory.Exists("/dev/shm"))
            Assert.Inconclusive("The mount-specific probe test requires Linux /dev/shm.");
        var shm = new DriveInfo("/dev/shm");
        var root = new DriveInfo("/");
        Assert.AreNotEqual(root.DriveFormat, shm.DriveFormat, "The test requires a destination filesystem distinct from root.");
        const long reserve = 64L * 1024 * 1024;
        using var mountFixture = new BackupFixture("/dev/shm");
        var expectedProbePath = Path.GetFullPath(mountFixture.StateDirectory);
        var lowCapacityProbePaths = new List<string>();
        var backup = new HorseIdentityMigrationManagedBackup(mountFixture.StateDirectory, path =>
        {
            var probePath = Path.GetFullPath(path);
            lowCapacityProbePaths.Add(probePath);
            return probePath == expectedProbePath ? reserve : long.MaxValue;
        });
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            backup.CreateAsync(mountFixture.OperationId, mountFixture.Sources, mountFixture.Journal));
        CollectionAssert.Contains(lowCapacityProbePaths, expectedProbePath,
            "Capacity admission must probe the nearest existing directory on the actual destination mount.");
        Assert.IsFalse(Directory.Exists(mountFixture.PackagePath), "The actual destination mount must fail capacity admission before package creation.");

        using var highCapacityFixture = new BackupFixture("/dev/shm");
        var highCapacityProbePaths = new List<string>();
        var highCapacityBackup = new HorseIdentityMigrationManagedBackup(highCapacityFixture.StateDirectory, path =>
        {
            highCapacityProbePaths.Add(Path.GetFullPath(path));
            return long.MaxValue;
        });
        await highCapacityBackup.CreateAsync(highCapacityFixture.OperationId, highCapacityFixture.Sources, highCapacityFixture.Journal);
        CollectionAssert.Contains(highCapacityProbePaths, Path.GetFullPath(highCapacityFixture.StateDirectory),
            "High available capacity on the actual destination mount must be accepted.");
        Assert.IsTrue(Directory.Exists(highCapacityFixture.PackagePath));
    }

    private sealed class BackupFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"horse-managed-backup-{Guid.NewGuid():N}");

        internal BackupFixture(string? baseDirectory = null)
        {
            var rootPath = baseDirectory is null ? _root : Path.Combine(baseDirectory, Path.GetFileName(_root));
            _root = rootPath;
            StateDirectory = Path.Combine(rootPath, "collection-state");
            Directory.CreateDirectory(StateDirectory);
            EventPath = Path.Combine(rootPath, "event.db");
            CollectionPath = Path.Combine(StateDirectory, "collection.db");
            SchedulePath = Path.Combine(rootPath, "schedule.db");
            Journal = new HorseIdentityMigrationJournal(StateDirectory, [EventPath, CollectionPath, SchedulePath]);
            CreateDatabase(EventPath, "event");
            CreateDatabase(CollectionPath, "collection");
            CreateDatabase(SchedulePath, "schedule");
            CreateDatabase(Journal.DatabasePath, "journal");
            File.WriteAllText(Journal.InitializationMarkerPath, HorseIdentityMigrationJournal.InitializationMarkerContent);
            Sources = new(EventPath, CollectionPath, SchedulePath, null);
            OperationId = Guid.NewGuid();
        }

        internal string Root => _root;
        internal string StateDirectory { get; }
        internal string EventPath { get; }
        internal string CollectionPath { get; }
        internal string SchedulePath { get; }
        internal string PackagePath => Path.Combine(StateDirectory, "backups", $"horse-id-migration-{OperationId:N}");
        internal Guid OperationId { get; }
        internal HorseIdentityMigrationJournal Journal { get; }
        internal HorseIdentityMigrationConfiguredSources Sources { get; }

        internal string SourceFor(string logicalName) => logicalName switch
        {
            "application/event-store.db" => EventPath,
            "application/collection-platform.db" => CollectionPath,
            "application/prediction-schedule.db" => SchedulePath,
            "ancillary/migration-journal.db" => Journal.DatabasePath,
            "ancillary/migration-journal-initialized.marker" => Journal.InitializationMarkerPath,
            _ => throw new AssertFailedException($"Unexpected backup member: {logicalName}"),
        };

        internal byte[] SnapshotSources() => new[] { EventPath, CollectionPath, SchedulePath, Journal.DatabasePath }
            .SelectMany(path => new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            .Append(Journal.InitializationMarkerPath)
            .SelectMany(path => File.Exists(path) ? new[] { path, Convert.ToBase64String(File.ReadAllBytes(path)) } : new[] { path, "<absent>" })
            .SelectMany(value => System.Text.Encoding.UTF8.GetBytes(value))
            .ToArray();

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
        }

        private static void CreateDatabase(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE sample(value TEXT NOT NULL); INSERT INTO sample VALUES ($value);";
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        internal async Task<string?> ReadSingleValueAsync(string path)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM sample;";
            return Convert.ToString(await command.ExecuteScalarAsync());
        }
    }
}
