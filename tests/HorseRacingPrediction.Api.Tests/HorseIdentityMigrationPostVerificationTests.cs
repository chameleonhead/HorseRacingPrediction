using EventFlow.Aggregates;
using EventFlow.EventStores;
using EventFlow;
using EventFlow.Commands;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using System.Text.Json;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationPostVerificationTests
{
    private const string OldHorseId = "horse-11111111-1111-4111-8111-111111111111";
    private const string RaceIdValue = "race-00000000-0000-4000-8000-000000000123";
    private const string MemoIdValue = "memo-00000000-0000-4000-8000-000000000124";
    private const string TicketIdValue = "predictionticket-00000000-0000-4000-8000-000000000125";

    [TestMethod]
    public async Task ComparesAllConfiguredStoresToTypedExpectedStateAndReplaysBackupRootsReadOnly()
    {
        await using var fixture = await CreateFixtureAsync();
        var mappings = CreateMappings();
        var (backup, journal, operationId, sources) = await CaptureAsync(fixture);
        await TransformSourcesAsync(fixture, journal, operationId, mappings);
        var before = SnapshotFiles(fixture);
        var services = fixture.EventFlowServices;
        var verifier = new HorseIdentityMigrationPostVerification(backup,
            services.GetRequiredService<IEventJsonSerializer>(),
            services.GetRequiredService<IDomainEventFactory>());

        var result = await verifier.VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None);

        Assert.AreEqual(4, result.ComparedDatabaseCount);
        Assert.IsTrue(result.ComparedRows > 0);
        Assert.IsTrue(result.ExpectedReplayTargets.Any(target => target.Kind == HorseIdentityMigrationReplayAggregateKind.Horse
            && target.AggregateId == mappings[0].DestinationHorseId));
        Assert.IsTrue(result.ExpectedReplayTargets.Any(target => target.Kind == HorseIdentityMigrationReplayAggregateKind.Race
            && target.AggregateId == RaceIdValue));
        Assert.IsTrue(result.ExpectedReplayTargets.Any(target => target.Kind == HorseIdentityMigrationReplayAggregateKind.Memo
            && target.AggregateId == MemoIdValue));
        Assert.IsTrue(result.ExpectedReplayTargets.Any(target => target.Kind == HorseIdentityMigrationReplayAggregateKind.PredictionTicket
            && target.AggregateId == TicketIdValue));
        CollectionAssert.AreEqual(before, SnapshotFiles(fixture), "Readonly checks must not alter DB or companion bytes/presence.");
    }

    [TestMethod]
    public async Task ComparesThreeConfiguredStoresWhenLocalQueueIsNotConfigured()
    {
        await using var fixture = await CreateFixtureAsync();
        var mappings = CreateMappings();
        var (backup, journal, operationId, sources) = await CaptureAsync(fixture, includeLocalQueue: false);
        await TransformSourcesAsync(fixture, journal, operationId, mappings);
        var queueBefore = SnapshotPathAndCompanions(fixture.QueuePath);

        var result = await CreateVerifier(fixture).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None);

        Assert.AreEqual(3, result.ComparedDatabaseCount);
        CollectionAssert.AreEqual(queueBefore, SnapshotPathAndCompanions(fixture.QueuePath),
            "An unconfigured local queue is neither compared nor opened.");
    }

    [TestMethod]
    public async Task RejectsNonIdentityDriftAndMissingRowsAfterSuccessfulTransformation()
    {
        await using var drift = await CreateFixtureAsync();
        var mappings = CreateMappings();
        var (backup, journal, operationId, sources) = await CaptureAsync(drift);
        await TransformSourcesAsync(drift, journal, operationId, mappings);
        await drift.ExecuteAsync(drift.EventStorePath,
            "UPDATE Horses SET RegisteredName='post-check drift' WHERE HorseId='" + mappings[0].DestinationHorseId + "';");
        var verifier = CreateVerifier(drift);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            verifier.VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));

        await using var missing = await CreateFixtureAsync();
        (backup, journal, operationId, sources) = await CaptureAsync(missing);
        await TransformSourcesAsync(missing, journal, operationId, mappings);
        await missing.ExecuteAsync(missing.EventStorePath,
            "DELETE FROM Horses WHERE HorseId='" + mappings[0].DestinationHorseId + "';");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(missing).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));
    }

    [TestMethod]
    public async Task RejectsSchemaMembershipAndNonemptyWalOrRollbackJournal()
    {
        await using var fixture = await CreateFixtureAsync();
        var mappings = CreateMappings();
        var (backup, journal, operationId, sources) = await CaptureAsync(fixture);
        await TransformSourcesAsync(fixture, journal, operationId, mappings);
        await fixture.ExecuteAsync(fixture.EventStorePath, "CREATE INDEX postcheck_extra ON Horses(RegisteredName);");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(fixture).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));

        await using var wal = await CreateFixtureAsync();
        (backup, journal, operationId, sources) = await CaptureAsync(wal);
        await TransformSourcesAsync(wal, journal, operationId, mappings);
        await File.WriteAllBytesAsync(wal.EventStorePath + "-wal", [1]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(wal).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));

        await using var rollback = await CreateFixtureAsync();
        (backup, journal, operationId, sources) = await CaptureAsync(rollback);
        await TransformSourcesAsync(rollback, journal, operationId, mappings);
        await File.WriteAllBytesAsync(rollback.EventStorePath + "-journal", [1]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(rollback).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));
    }

    [TestMethod]
    public async Task RejectsExtraPhysicalRowsAndTablesBeforeReplay()
    {
        await using var extraRow = await CreateFixtureAsync();
        var mappings = CreateMappings();
        var (backup, journal, operationId, sources) = await CaptureAsync(extraRow);
        await TransformSourcesAsync(extraRow, journal, operationId, mappings);
        await extraRow.ExecuteAsync(extraRow.EventStorePath, """
            INSERT INTO Horses (HorseId, RegisteredName, NormalizedName, Aliases)
            VALUES ('horse-22222222-2222-4222-8222-222222222222', 'Extra horse', 'extrahorse', '[]');
            """);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(extraRow).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));

        await using var extraTable = await CreateFixtureAsync();
        (backup, journal, operationId, sources) = await CaptureAsync(extraTable);
        await TransformSourcesAsync(extraTable, journal, operationId, mappings);
        await extraTable.ExecuteAsync(extraTable.EventStorePath, "CREATE TABLE postcheck_extra(value TEXT);");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(extraTable).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));
    }

    [TestMethod]
    public async Task RejectsEventMetadataSequenceAndMalformedDataDriftBeforeReplay()
    {
        var mappings = CreateMappings();
        foreach (var mutation in new[] { "metadata", "sequence", "malformed" })
        {
            await using var fixture = await CreateFixtureAsync();
            var (backup, journal, operationId, sources) = await CaptureAsync(fixture);
            await TransformSourcesAsync(fixture, journal, operationId, mappings);
            var sql = mutation switch
            {
                "metadata" => "UPDATE EventEntity SET Metadata='{}' WHERE AggregateName='HorseAggregate';",
                "sequence" => "UPDATE EventEntity SET AggregateSequenceNumber=AggregateSequenceNumber+1 WHERE AggregateName='HorseAggregate';",
                _ => "UPDATE EventEntity SET Data='{' WHERE AggregateName='HorseAggregate';",
            };
            await fixture.ExecuteAsync(fixture.EventStorePath, sql);
            var before = SnapshotFiles(fixture);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                CreateVerifier(fixture).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));
            CollectionAssert.AreEqual(before, SnapshotFiles(fixture), "Rejected readonly checks preserve DB/WAL/SHM/journal bytes and presence.");
        }
    }

    [TestMethod]
    public async Task RejectsChangedColumnFacetAndOversizedDdlWithinBoundedSchemaInspection()
    {
        await using var changedFacet = await CreateFixtureAsync();
        var mappings = CreateMappings();
        var (backup, journal, operationId, sources) = await CaptureAsync(changedFacet);
        await TransformSourcesAsync(changedFacet, journal, operationId, mappings);
        await changedFacet.ExecuteAsync(changedFacet.EventStorePath, "ALTER TABLE EventEntity ADD COLUMN postcheck TEXT;");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(changedFacet).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));

        await using var oversizedDdl = await CreateFixtureAsync();
        (backup, journal, operationId, sources) = await CaptureAsync(oversizedDdl);
        await TransformSourcesAsync(oversizedDdl, journal, operationId, mappings);
        var columns = string.Join(",", Enumerable.Range(0, 1200).Select(index => $"column_{index:D4} TEXT"));
        await oversizedDdl.ExecuteAsync(oversizedDdl.EventStorePath, $"CREATE TABLE postcheck_oversized({columns});");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(oversizedDdl).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));
    }

    [TestMethod]
    public async Task RejectsSqliteSequenceDrift()
    {
        await using var fixture = await CreateFixtureAsync();
        var mappings = CreateMappings();
        var (backup, journal, operationId, sources) = await CaptureAsync(fixture);
        await TransformSourcesAsync(fixture, journal, operationId, mappings);
        await using var connection = new SqliteConnection($"Data Source={fixture.EventStorePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sqlite_sequence SET seq=seq+1 WHERE name='EventEntity';";
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync(), "The real EventFlow event table uses AUTOINCREMENT.");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CreateVerifier(fixture).VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));
    }

    [TestMethod]
    public async Task RejectsEmptyMappingAndMissingConfiguredSourceClosed()
    {
        await using var fixture = await CreateFixtureAsync();
        var mappings = CreateMappings();
        var (backup, journal, operationId, sources) = await CaptureAsync(fixture);
        var verifier = CreateVerifier(fixture);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            verifier.VerifyAsync(operationId, sources, journal, [], CancellationToken.None));
        File.Delete(fixture.CollectionPath);
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            verifier.VerifyAsync(operationId, sources, journal, mappings, CancellationToken.None));
    }

    private static async Task<HorseIdentityMigrationStoreEngineTests.StoreFixture> CreateFixtureAsync()
    {
        var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, "DELETE FROM EventEntity;");
        await SeedReplayableHorseEventAsync(fixture);
        await fixture.SeedJraResourceAsync(OldHorseId);
        var queueOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        queueOptions.Converters.Add(new HorseRacingPrediction.Contracts.Common.Time.JstStoredDateTimeOffsetJsonConverter());
        var queueBody = JsonSerializer.Serialize(new HorseRacingPrediction.CollectionOperations.CollectionPlatform
            .CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "post-check"), queueOptions);
        await fixture.SeedLocalQueueMessageAsync(queueBody);
        return fixture;
    }

    private static async Task SeedReplayableHorseEventAsync(
        HorseIdentityMigrationStoreEngineTests.StoreFixture fixture)
    {
        var dbProvider = new SqliteDbContextProvider($"Data Source={fixture.EventStorePath};Pooling=False");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dbProvider);
        services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(
            provider => provider.GetRequiredService<SqliteDbContextProvider>());
        services.AddEventFlow(options => options
            .AddDefaults(typeof(HorseAggregate).Assembly)
            .AddDefaults(typeof(RegisterHorseCommand).Assembly)
            .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
            .UseEntityFrameworkEventStore<EventStoreDbContext>());
        try
        {
            await using (var context = dbProvider.CreateContext())
                await context.Database.MigrateAsync();
            using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<ICommandBus>().PublishAsync(
                new RegisterHorseCommand(new HorseId(OldHorseId), "Fixture Horse", "fixture horse"),
                CancellationToken.None);
            var commandBus = provider.GetRequiredService<ICommandBus>();
            var raceId = new RaceId(RaceIdValue);
            await commandBus.PublishAsync(new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14),
                "TOKYO", 1, "Fixture Race"), CancellationToken.None);
            await commandBus.PublishAsync(new PublishRaceCardCommand(raceId, 0), CancellationToken.None);
            var memoId = new MemoId(MemoIdValue);
            await commandBus.PublishAsync(new CreateMemoCommand(memoId, "fixture", "Note", "before",
                DateTimeOffset.Parse("2026-10-14T00:00:00Z"),
                [new(MemoSubjectType.Horse, OldHorseId)]), CancellationToken.None);
            await commandBus.PublishAsync(new UpdateMemoCommand(memoId, content: "after"), CancellationToken.None);
            var ticketId = new PredictionTicketId(TicketIdValue);
            await commandBus.PublishAsync(new CreatePredictionTicketCommand(ticketId, RaceIdValue, "fixture", "model",
                .5m, "summary"), CancellationToken.None);
            await commandBus.PublishAsync(new AddPredictionMarkCommand(ticketId, "entry-1", "A", 1, 1m, null),
                CancellationToken.None);
        }
        finally
        {
            dbProvider.Dispose();
        }
    }

    private static async Task<(HorseIdentityMigrationManagedBackup Backup, HorseIdentityMigrationJournal Journal,
        Guid OperationId, HorseIdentityMigrationConfiguredSources Sources)> CaptureAsync(
        HorseIdentityMigrationStoreEngineTests.StoreFixture fixture, bool includeLocalQueue = true)
    {
        var journal = new HorseIdentityMigrationJournal(Path.Combine(fixture.Root, "migration-journal"),
            [fixture.EventStorePath, fixture.CollectionPath, fixture.SchedulePath, fixture.QueuePath]);
        Directory.CreateDirectory(Path.GetDirectoryName(journal.DatabasePath)!);
        await using (var connection = new SqliteConnection($"Data Source={journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
        }
        await File.WriteAllTextAsync(journal.InitializationMarkerPath,
            HorseIdentityMigrationJournal.InitializationMarkerContent);
        var sources = new HorseIdentityMigrationConfiguredSources(fixture.EventStorePath, fixture.CollectionPath,
            fixture.SchedulePath, includeLocalQueue ? fixture.QueuePath : null);
        var operationId = Guid.NewGuid();
        var backup = HorseIdentityMigrationManagedBackup.ForJournal(journal, _ => long.MaxValue);
        await backup.CreateAsync(operationId, sources, journal);
        return (backup, journal, operationId, sources);
    }

    private static async Task TransformSourcesAsync(HorseIdentityMigrationStoreEngineTests.StoreFixture fixture,
        HorseIdentityMigrationJournal journal, Guid operationId, IReadOnlyList<HorseIdentityMigrationMapping> mappings)
    {
        var manifest = new HorseIdentityMigrationManifest(
            [new HorseIdentityMigrationManifestEntry(OldHorseId, "jra-horse:2024100828", HypothesisAccepted: true)]);
        Assert.IsTrue((await journal.StageAsync(operationId, manifest, DateTimeOffset.UtcNow)).Succeeded);
        var fingerprint = new string('B', 64);
        Assert.IsTrue((await journal.BindStandaloneRequestAsync(operationId, manifest, fingerprint,
            DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var snapshot = await fixture.CreateEngineWithEventFlow("Local")
            .InspectExecutionInputsAsync(manifest, CancellationToken.None);
        var result = await new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow("Local"))
            .ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None);
        Assert.AreEqual(TypedHorseIdentityExecutionStatus.Completed, result.Status,
            result.Reason + "; " + snapshot.TypedReferences?.Failure);
        Assert.AreEqual(mappings[0].DestinationHorseId, await ReadHorseIdAsync(fixture.EventStorePath));
    }

    private static IReadOnlyList<HorseIdentityMigrationMapping> CreateMappings()
    {
        var result = HorseIdentityMigrationManifestPolicy.Validate(new HorseIdentityMigrationManifest(
            [new HorseIdentityMigrationManifestEntry(OldHorseId, "jra-horse:2024100828", HypothesisAccepted: true)]));
        Assert.IsTrue(result.IsValid);
        return result.Mappings;
    }

    private static HorseIdentityMigrationPostVerification CreateVerifier(
        HorseIdentityMigrationStoreEngineTests.StoreFixture fixture) => new(
        HorseIdentityMigrationManagedBackup.ForJournal(
            new HorseIdentityMigrationJournal(Path.Combine(fixture.Root, "migration-journal"),
                [fixture.EventStorePath, fixture.CollectionPath, fixture.SchedulePath, fixture.QueuePath]), _ => long.MaxValue),
        fixture.EventFlowServices.GetRequiredService<IEventJsonSerializer>(),
        fixture.EventFlowServices.GetRequiredService<IDomainEventFactory>());

    private static async Task<string> ReadHorseIdAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT HorseId FROM Horses;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static byte[] SnapshotFiles(HorseIdentityMigrationStoreEngineTests.StoreFixture fixture)
    {
        using var stream = new MemoryStream();
        foreach (var path in new[] { fixture.EventStorePath, fixture.CollectionPath, fixture.SchedulePath, fixture.QueuePath })
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
            {
                var file = path + suffix;
                var exists = File.Exists(file);
                stream.WriteByte(exists ? (byte)1 : (byte)0);
                if (exists) stream.Write(File.ReadAllBytes(file));
            }
        return System.Security.Cryptography.SHA256.HashData(stream.ToArray());
    }

    private static byte[] SnapshotPathAndCompanions(string databasePath)
    {
        using var stream = new MemoryStream();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            var path = databasePath + suffix;
            var exists = File.Exists(path);
            stream.WriteByte(exists ? (byte)1 : (byte)0);
            if (exists) stream.Write(File.ReadAllBytes(path));
        }
        return System.Security.Cryptography.SHA256.HashData(stream.ToArray());
    }
}
