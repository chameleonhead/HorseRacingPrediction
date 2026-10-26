using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationDeployInteropTests
{
    private const string FixtureDirectoryVariable = "HRP_HORSE_IDENTITY_DEPLOY_INTEROP_FIXTURE_DIR";
    private const string FixtureDirectoryPrefix = "hrp-horse-identity-deploy-interop-";
    private static readonly Guid OperationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid RequestKey = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [TestMethod]
    public async Task FixtureProducer_UsesRealPendingRequestIntentGatesAndPauseProducer()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), FixtureDirectoryPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            await WriteFixtureAsync(temporaryRoot);
            AssertFixtureFiles(temporaryRoot);
        }
        finally
        {
            DeleteCheckedTemporaryDirectory(temporaryRoot);
        }
    }

    [TestMethod]
    public async Task ExportProductionFixtureWhenRequested()
    {
        var requestedDirectory = Environment.GetEnvironmentVariable(FixtureDirectoryVariable);
        if (string.IsNullOrWhiteSpace(requestedDirectory)) return;

        var outputDirectory = Path.GetFullPath(requestedDirectory);
        AssertOwnedTemporaryDirectory(outputDirectory);
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            throw new InvalidOperationException("The owned deployment fixture directory must be empty before export.");

        await WriteFixtureAsync(outputDirectory);
        AssertFixtureFiles(outputDirectory);
        Console.WriteLine("PASS typed deployment fixture sources=3 gates=3 pendingV2=true intent=true pause=true");
    }

    private static async Task WriteFixtureAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        if (Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            throw new InvalidOperationException("The deployment fixture producer only writes to an empty directory.");

        const string oldHorseId = "horse-11111111-1111-4111-8111-111111111111";
        const string raceId = "202609190101";
        const string ticketId = "predictionticket-00000000-0000-4000-8000-000000000201";
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await stores.SeedHorseAsync(oldHorseId);
        await stores.SeedJraResourceAsync(oldHorseId);
        var entryId = HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator.BuildRaceEntryId(raceId, oldHorseId);
        await stores.SeedInterleavedTicketStreamAcrossPageBoundaryAsync(entryId);
        await HorseIdentityMigrationRuntimeModeTests.ReplaceSupportedEventsWithCommandBusSeedAsync(
            stores, oldHorseId, raceId, ticketId, entryId);

        var sourcePaths = new[]
        {
            Path.Combine(outputDirectory, "eventstore.db"),
            Path.Combine(outputDirectory, "collection-platform.db"),
            Path.Combine(outputDirectory, "prediction-executions.db"),
        };
        File.WriteAllText(
            Path.Combine(outputDirectory, "operator-source-paths.json"),
            JsonSerializer.Serialize(sourcePaths));
        File.Copy(stores.EventStorePath, sourcePaths[0]);
        File.Copy(stores.CollectionPath, sourcePaths[1]);
        File.Copy(stores.SchedulePath, sourcePaths[2]);

        var collectionStore = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = outputDirectory,
            DatabaseFileName = Path.GetFileName(sourcePaths[1]),
        }));
        var pauseStartedAt = DateTimeOffset.UtcNow.AddMinutes(-16);
        var pauseStartedAtWholeSecond = pauseStartedAt.Ticks - pauseStartedAt.Ticks % TimeSpan.TicksPerSecond;
        pauseStartedAt = new DateTimeOffset(
            pauseStartedAtWholeSecond + 8_505_980, pauseStartedAt.Offset);
        await collectionStore.SetPausedAsync(true, "isolated deploy interoperability fixture", pauseStartedAt);
        var pipeline = await collectionStore.GetPipelineStateAsync();
        if (!pipeline.IsPaused || pipeline.UpdatedAt is null
            || pipeline.UpdatedAt.Value != pauseStartedAt
            || pipeline.UpdatedAt.Value > DateTimeOffset.UtcNow.AddMinutes(-15))
            throw new InvalidOperationException("The real collection store did not persist the exact aged pause timestamp.");

        await using (var timestampConnection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={sourcePaths[1]};Mode=ReadOnly;Pooling=False"))
        {
            await timestampConnection.OpenAsync();
            await using var timestampCommand = timestampConnection.CreateCommand();
            timestampCommand.CommandText = "SELECT UpdatedAt FROM collection_platform_controls WHERE ControlId='pipeline'";
            var persistedTimestamp = (string?)await timestampCommand.ExecuteScalarAsync();
            if (persistedTimestamp is null
                || !System.Text.RegularExpressions.Regex.IsMatch(
                    persistedTimestamp, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.850598$"))
                throw new InvalidOperationException(
                    "The real collection store did not preserve the deterministic native six-digit timestamp representation.");
        }

        var journal = new HorseIdentityMigrationJournal(outputDirectory, sourcePaths);
        var manifest = new HorseIdentityMigrationManifest(
        [new HorseIdentityMigrationManifestEntry(oldHorseId, "jra-horse:1234567890", true)]);
        var immutableRequestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        var now = DateTimeOffset.UtcNow;
        var staged = await journal.StagePendingWithRequestKeyAsync(
            OperationId, manifest, RequestKey, immutableRequestHash, now);
        if (!staged.Succeeded)
            throw new InvalidOperationException("The real PendingV2 journal producer could not stage the fixture.");
        var createdIntent = await journal.CreateExecutionIntentAsync(OperationId, immutableRequestHash, now);
        if (!createdIntent.Succeeded)
            throw new InvalidOperationException("The real journal producer could not create the execution intent.");
        var advancedIntent = await journal.AdvanceExecutionIntentAsync(
            OperationId, HorseIdentityMigrationExecutionPhase.Preparing, now);
        if (!advancedIntent.Succeeded)
            throw new InvalidOperationException("The real journal producer could not advance the fixture intent.");
        var startupGuard = await journal.InspectStartupGuardAsync(now.AddSeconds(1));
        if (startupGuard.State == HorseIdentityMigrationStartupState.Corrupt)
            throw new InvalidOperationException("The real journal startup guard could not read the typed fixture.");

        using var runtimeOwnership = HorseIdentityMigrationRuntimeOwnership.Acquire(sourcePaths);
        using var controlLease = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(sourcePaths);
        await HorseIdentityMigrationMaintenanceOwnership.InstallStandaloneMaintenanceGatesAsync(
            runtimeOwnership, controlLease, OperationId, immutableRequestHash, journal);

        var persistedIntent = await journal.GetExecutionIntentAsync(OperationId);
        if (persistedIntent?.ImmutableRequestHash != immutableRequestHash
            || persistedIntent.Phase != HorseIdentityMigrationExecutionPhase.Preparing
            || !HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(
                sourcePaths, OperationId, immutableRequestHash,
                HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths)))
            throw new InvalidOperationException("The typed fixture did not persist a matching intent and complete gate set.");
    }

    private static void AssertFixtureFiles(string directory)
    {
        var requiredFiles = new[]
        {
            "eventstore.db",
            "collection-platform.db",
            "prediction-executions.db",
            "horse-identity-migration-journal.db",
            HorseIdentityMigrationJournal.InitializationMarkerFileName,
            "horse-identity-migration-operation.lock",
            "operator-source-paths.json",
            ".eventstore.db.horse-identity-migration.maintenance",
            ".collection-platform.db.horse-identity-migration.maintenance",
            ".prediction-executions.db.horse-identity-migration.maintenance",
            ".eventstore.db.horse-identity-migration.supervisor-control.bootstrap.owner",
            ".collection-platform.db.horse-identity-migration.supervisor-control.bootstrap.owner",
            ".prediction-executions.db.horse-identity-migration.supervisor-control.bootstrap.owner",
        };
        foreach (var file in requiredFiles)
            if (!File.Exists(Path.Combine(directory, file)))
                throw new InvalidOperationException($"The typed deployment fixture is missing required file '{file}'.");
    }

    private static void AssertOwnedTemporaryDirectory(string directory)
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(directory).StartsWith(FixtureDirectoryPrefix, StringComparison.Ordinal)
            || (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidOperationException("The deployment fixture must target an owned temporary directory.");
    }

    private static void DeleteCheckedTemporaryDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        AssertOwnedTemporaryDirectory(Path.GetFullPath(directory));
        Directory.Delete(directory, recursive: true);
    }
}
