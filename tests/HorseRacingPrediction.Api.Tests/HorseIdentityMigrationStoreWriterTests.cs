using System.Text.Json;
using EventFlow.Aggregates;
using EventFlow.EventStores;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationStoreWriterTests
{
    private const string OldHorseId = "horse-11111111-1111-4111-8111-111111111111";
    private const string RaceId = "202609190101";
    private const string TicketAggregateId = "predictionticket-00000000-0000-4000-8000-000000000201";

    [TestMethod]
    public void TypedProjectionRecipeRebuildsOnlyTheExactDerivedEntryId()
    {
        var mapping = Mapping();
        var oldEntryId = DeterministicIdGenerator.BuildRaceEntryId(RaceId, OldHorseId);
        var manualId = "legacy-entry:manual-17";
        var rows = new[]
        {
            new RacePredictionContextEntry(oldEntryId, OldHorseId, 3, "jockey-7", null, 2,
                56.5m, "M", 4, 500m, 2m, "front", "Preserve Owner"),
            new RacePredictionContextEntry(manualId, OldHorseId, 4, "jockey-8", null, 3,
                55m, "F", 5, 480m, -1m, null, "Manual Reference Owner"),
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var source = JsonSerializer.Serialize(rows, options);

        Assert.IsTrue(TypedHorseIdentityReferenceReader.TryTransformKnownJsonForExecution(
            "RacePredictionContexts", "Entries", source, RaceId, RaceId, [mapping], out var transformed));

        var actual = JsonSerializer.Deserialize<RacePredictionContextEntry[]>(transformed, options)!;
        var destination = mapping.DestinationHorseId;
        Assert.AreEqual(destination, actual[0].HorseId);
        Assert.AreEqual(DeterministicIdGenerator.BuildRaceEntryId(RaceId, destination), actual[0].EntryId);
        Assert.AreEqual(destination, actual[1].HorseId);
        Assert.AreEqual(manualId, actual[1].EntryId, "Manual IDs are immutable even when HorseId maps.");
        Assert.AreEqual(rows[0] with { HorseId = destination, EntryId = actual[0].EntryId }, actual[0]);
        Assert.AreEqual(rows[1] with { HorseId = destination }, actual[1]);
    }

    [TestMethod]
    public void TicketMarkRecipeMapsExactDerivedIdsAndLeavesManualIdsAndTextAlone()
    {
        var mapping = Mapping();
        var oldEntryId = DeterministicIdGenerator.BuildRaceEntryId(RaceId, OldHorseId);
        var marks = new[]
        {
            new PredictionMarkSnapshot(oldEntryId, "◎", 1, 9.5m, "keep user comment"),
            new PredictionMarkSnapshot("manual:entry-22", "○", 2, 8m, "keep this too"),
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var source = JsonSerializer.Serialize(marks, options);

        Assert.IsTrue(TypedHorseIdentityReferenceReader.TryTransformKnownJsonForExecution(
            "PredictionTickets", "Marks", source, "ticket-id", RaceId, [mapping], out var transformed));

        var actual = JsonSerializer.Deserialize<PredictionMarkSnapshot[]>(transformed, options)!;
        Assert.AreEqual(DeterministicIdGenerator.BuildRaceEntryId(RaceId, mapping.DestinationHorseId),
            actual[0].EntryId);
        Assert.AreEqual(marks[0] with { EntryId = actual[0].EntryId }, actual[0]);
        Assert.AreEqual(marks[1], actual[1]);
    }

    [TestMethod]
    public void UnlistedJsonRoleAndSubstringLikeHorseValueAreNotRewritten()
    {
        var mapping = Mapping();
        var source = "{\"note\":\"" + OldHorseId + "\"}";

        Assert.IsFalse(TypedHorseIdentityReferenceReader.TryTransformKnownJsonForExecution(
            "UnlistedTable", "Payload", source, "row", null, [mapping], out var unchanged));
        Assert.AreEqual(source, unchanged);
        Assert.AreEqual("prefix-" + OldHorseId + "-suffix",
            TypedHorseIdentityReferenceReader.TransformExactHorseReferenceForExecution(
                "prefix-" + OldHorseId + "-suffix", [mapping]));
    }

    [TestMethod]
    public void CollectionMetadataRecipeChangesOnlyDeclaredHorseRoles()
    {
        var mapping = Mapping();
        var source = "{\"requestedByHorseId\":\"" + OldHorseId
            + "\",\"discoveredFromType\":\"Horse\",\"discoveredFromProvider\":\"JRA\",\"discoveredFromId\":\""
            + OldHorseId + "\",\"discoveryAncestors\":\"" + OldHorseId
            + "|keep\",\"userNote\":\"" + OldHorseId + "\"}";

        Assert.IsTrue(TypedHorseIdentityReferenceReader.TryTransformCollectionMetadataForExecution(
            source, [mapping], out var transformed));

        using var actual = JsonDocument.Parse(transformed);
        var destination = mapping.DestinationHorseId;
        Assert.AreEqual(destination, actual.RootElement.GetProperty("requestedByHorseId").GetString());
        Assert.AreEqual(destination, actual.RootElement.GetProperty("discoveredFromId").GetString());
        Assert.AreEqual(destination + "|keep", actual.RootElement.GetProperty("discoveryAncestors").GetString());
        Assert.AreEqual(OldHorseId, actual.RootElement.GetProperty("userNote").GetString());
    }

    [TestMethod]
    public async Task OwnedExecutionWritesAndVerifiesARealSqliteHorseAndEventStream()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(OldHorseId);
        var oldEntryId = DeterministicIdGenerator.BuildRaceEntryId(RaceId, OldHorseId);
        await fixture.SeedInterleavedTicketStreamAcrossPageBoundaryAsync(oldEntryId);
        await fixture.ExecuteAsync(fixture.CollectionPath, $$"""
            UPDATE collection_resources
            SET AttributesJson='{"requestedByHorseId":"{{OldHorseId}}","userNote":"{{OldHorseId}}"}'
            WHERE Type='Horse' AND Provider='JRA' AND ResourceId='{{OldHorseId}}';
            """);
        var queueOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        queueOptions.Converters.Add(new HorseRacingPrediction.Contracts.Common.Time.JstStoredDateTimeOffsetJsonConverter());
        var queueBody = JsonSerializer.Serialize(new HorseRacingPrediction.CollectionOperations.CollectionPlatform
            .CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "preserve-token"), queueOptions);
        await fixture.SeedLocalQueueMessageAsync(queueBody);
        await fixture.ExecuteAsync(fixture.SchedulePath, """
            INSERT INTO prediction_candidates
                (RaceId, Status, FirstQueuedAt, AvailableAt, LeaseToken, LeaseExpiresAt, LastError, UpdatedAt)
            VALUES ('preserve-schedule-race', 0, '2026-10-14T00:00:00+09:00',
                    '2026-10-14T00:00:00+09:00', NULL, NULL, 'preserve error', '2026-10-14T00:00:00+09:00');
            """);
        await fixture.ExecuteAsync(fixture.EventStorePath, $$"""
            INSERT INTO JraSubjectProfileReadModel
                (rowid, SubjectId, Name, SourceIdentity, SourceUrl, AcquiredAt, Fields)
            VALUES (0, '{{OldHorseId}}', 'preserve name', 'preserve source', 'https://example.invalid/profile',
                    '2026-10-14T00:00:00+09:00', '{"display":"preserve"}');
            INSERT INTO MemoSubjects (rowid, SubjectKey, Memos)
            VALUES (-1, 'Horse:{{OldHorseId}}', '[]');
            INSERT INTO PredictionTickets
                (PredictionTicketId, RaceId, PredictorType, PredictorId, ConfidenceScore, SummaryComment,
                 PredictedAt, TicketStatus, Marks, Evaluations, EvaluationStatus)
            VALUES ('{{TicketAggregateId}}', '{{RaceId}}', 'fixture', 'preserve-predictor', 0.5, 'preserve comment',
                    '2026-10-14T00:00:00+09:00', 0,
                    '[{"entryId":"{{oldEntryId}}","markCode":"A","predictedRank":1,"score":0.9,"comment":"preserve mark"}]',
                    '[]', 0);
            """);
        var manifest = Manifest();
        var destination = HorseIdentityMigrationManifestPolicy.Validate(manifest).Mappings.Single().DestinationHorseId;
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        await StageBoundV1Async(journal, operationId, manifest);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow("Local"));

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None);

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.Completed, result.Status);
        Assert.IsTrue(result.CanWrite);
        Assert.IsTrue(result.VerifiedPageCount > 0);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await journal.GetStatusAsync(operationId))!.Status);
        var eventRows = await fixture.ReadEventRowsAsync();
        Assert.IsTrue(eventRows.Count > 128, "The registered ticket context is split across physical keyset pages.");
        var transformedMarkRow = eventRows.Single(row => row.Record.AggregateId == TicketAggregateId
            && row.Record.AggregateSequenceNumber == 2);
        Assert.AreEqual(129L, transformedMarkRow.RowId);
        var serializer = fixture.EventFlowServices.GetRequiredService<EventFlow.EventStores.IEventJsonSerializer>();
        var transformedMark = (HorseRacingPrediction.Domain.Predictions.PredictionMarkAdded)
            serializer.Deserialize(transformedMarkRow.Record.Data, transformedMarkRow.Record.Metadata).GetAggregateEvent();
        Assert.AreEqual(DeterministicIdGenerator.BuildRaceEntryId(RaceId, destination), transformedMark.EntryId);
        Assert.AreEqual("preserve event comment", transformedMark.Comment);
        await using var connection = new SqliteConnection($"Data Source={fixture.EventStorePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        Assert.AreEqual(destination, await ScalarAsync(connection, "SELECT HorseId FROM Horses;"));
        Assert.AreEqual(destination, await ScalarAsync(connection, "SELECT AggregateId FROM EventEntity;"));
        Assert.AreEqual(destination, await ScalarAsync(connection,
            "SELECT SubjectId FROM JraSubjectProfileReadModel;"));
        Assert.AreEqual(0L, Convert.ToInt64(await ScalarAsync(connection,
            "SELECT rowid FROM JraSubjectProfileReadModel;")), "Zero SQLite rowids are valid page members.");
        Assert.AreEqual("preserve name", await ScalarAsync(connection,
            "SELECT Name FROM JraSubjectProfileReadModel;"));
        Assert.AreEqual(-1L, Convert.ToInt64(await ScalarAsync(connection,
            "SELECT rowid FROM MemoSubjects;")), "Negative SQLite rowids are valid page members.");
        Assert.AreEqual($"Horse:{destination}", await ScalarAsync(connection,
            "SELECT SubjectKey FROM MemoSubjects;"));
        var marksJson = (string)(await ScalarAsync(connection,
            $"SELECT Marks FROM PredictionTickets WHERE PredictionTicketId='{TicketAggregateId}';"))!;
        var marks = JsonSerializer.Deserialize<PredictionMarkSnapshot[]>(marksJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual(DeterministicIdGenerator.BuildRaceEntryId(RaceId, destination), marks.Single().EntryId);
        Assert.AreEqual("preserve mark", marks.Single().Comment);
        await using var collection = new SqliteConnection($"Data Source={fixture.CollectionPath};Pooling=False;Mode=ReadOnly");
        await collection.OpenAsync();
        Assert.AreEqual(destination, await ScalarAsync(collection,
            "SELECT ResourceId FROM collection_resources WHERE Type='Horse' AND Provider='JRA';"));
        using var attributes = JsonDocument.Parse((string)(await ScalarAsync(collection,
            "SELECT AttributesJson FROM collection_resources WHERE Type='Horse' AND Provider='JRA';"))!);
        Assert.AreEqual(destination, attributes.RootElement.GetProperty("requestedByHorseId").GetString());
        Assert.AreEqual(OldHorseId, attributes.RootElement.GetProperty("userNote").GetString());
        await using var schedule = new SqliteConnection($"Data Source={fixture.SchedulePath};Pooling=False;Mode=ReadOnly");
        await schedule.OpenAsync();
        Assert.AreEqual("preserve error", await ScalarAsync(schedule,
            "SELECT LastError FROM prediction_candidates WHERE RaceId='preserve-schedule-race';"));
        await using var queue = new SqliteConnection($"Data Source={fixture.QueuePath};Pooling=False;Mode=ReadOnly");
        await queue.OpenAsync();
        Assert.AreEqual(queueBody, await ScalarAsync(queue, "SELECT body FROM local_collection_messages;"));
    }

    [TestMethod]
    public async Task PendingExecutionWithoutValidatedBackupProofIsRejectedBeforeLeaseOrSourceWrite()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var manifest = Manifest();
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
            requestHash, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash, DateTimeOffset.UtcNow)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
        })
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)).Succeeded);
        var sourceSetHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(
            [fixture.EventStorePath, fixture.CollectionPath, fixture.SchedulePath, fixture.QueuePath]);
        Assert.IsTrue((await journal.SealPendingRequestAsync(operationId, requestHash, new string('A', 64),
            sourceSetHash, new string('B', 64), DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Applying, DateTimeOffset.UtcNow)).Succeeded);
        var before = await fixture.SourceSnapshotAsync();
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow("Local"));

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            authorizedSourceSetHash: sourceSetHash);

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.RejectedBeforeWrite, result.Status);
        Assert.IsFalse(result.CanWrite);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged,
            (await journal.GetStatusAsync(operationId))!.Status);
        Assert.AreEqual(before, await fixture.SourceSnapshotAsync(),
            "The centralized pending-seal gate must run before ownership acquisition and all source writes.");
        Assert.IsFalse(File.Exists(journal.LockPath), "A rejected pending request must not acquire the lease lock.");
    }

    [TestMethod]
    public async Task PendingSourceDriftAfterSealIsRejectedBeforeAnyBusinessWrite()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(OldHorseId);
        await fixture.SeedInterleavedTicketStreamAcrossPageBoundaryAsync(
            DeterministicIdGenerator.BuildRaceEntryId(RaceId, OldHorseId));
        _ = new LocalCollectionQueue(fixture.QueuePath);
        await fixture.ExecuteAsync(fixture.CollectionPath, $$"""
            UPDATE collection_resources
            SET AttributesJson='{"requestedByHorseId":"{{OldHorseId}}","userNote":"before seal"}'
            WHERE Type='Horse' AND Provider='JRA' AND ResourceId='{{OldHorseId}}';
            """);
        var queueOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        queueOptions.Converters.Add(new HorseRacingPrediction.Contracts.Common.Time.JstStoredDateTimeOffsetJsonConverter());
        var queueBody = JsonSerializer.Serialize(new HorseRacingPrediction.CollectionOperations.CollectionPlatform
            .CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "pending drift fixture"), queueOptions);
        await fixture.SeedLocalQueueMessageAsync(queueBody);
        await fixture.ExecuteAsync(fixture.SchedulePath, """
            INSERT INTO prediction_candidates
                (RaceId, Status, FirstQueuedAt, AvailableAt, LeaseToken, LeaseExpiresAt, LastError, UpdatedAt)
            VALUES ('pending-drift-race', 0, '2026-10-14T00:00:00+09:00',
                    '2026-10-14T00:00:00+09:00', NULL, NULL, 'preserve error', '2026-10-14T00:00:00+09:00');
            """);
        var manifest = Manifest();
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
            requestHash, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow("Local"));
        var preSealPreview = await engine.InspectPreviewAsync(manifest, CancellationToken.None);
        Assert.IsTrue(preSealPreview.CanStage,
            $"Blockers: {string.Join(',', preSealPreview.Blockers)}; schemas: {string.Join(',', preSealPreview.Stores.Select(item => item.Surface + ":" + item.Status))}");
        Assert.IsNotNull(preSealPreview.Fingerprint);
        Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash, DateTimeOffset.UtcNow)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
        })
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)).Succeeded);
        var sourcePaths = new[] { fixture.EventStorePath, fixture.CollectionPath, fixture.SchedulePath, fixture.QueuePath };
        var sourceSetHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths);
        var backupManifestHash = new string('B', 64);
        Assert.IsTrue((await journal.SealPendingRequestAsync(operationId, requestHash, preSealPreview.Fingerprint!,
            sourceSetHash, backupManifestHash, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Applying, DateTimeOffset.UtcNow)).Succeeded);

        await fixture.ExecuteAsync(fixture.CollectionPath, $$"""
            UPDATE collection_resources
            SET AttributesJson='{"requestedByHorseId":"{{OldHorseId}}","userNote":"changed after seal"}'
            WHERE Type='Horse' AND Provider='JRA' AND ResourceId='{{OldHorseId}}';
            """);
        var afterDrift = await fixture.SourceSnapshotAsync();
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            authorizedSourceSetHash: sourceSetHash, authorizedBackupManifestHash: backupManifestHash);

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.RejectedBeforeWrite, result.Status);
        Assert.IsFalse(result.CanWrite);
        Assert.AreEqual(afterDrift, await fixture.SourceSnapshotAsync(),
            "The complete fresh fingerprint must match the sealed census before the writer can plan or mutate pages.");
        await using var connection = new SqliteConnection($"Data Source={fixture.CollectionPath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        Assert.AreEqual("{\"requestedByHorseId\":\"" + OldHorseId + "\",\"userNote\":\"changed after seal\"}",
            await ScalarAsync(connection, "SELECT AttributesJson FROM collection_resources WHERE ResourceId='" + OldHorseId + "';"));
    }

    [TestMethod]
    public async Task PendingUnchangedSourcesExecuteAgainstSealedPreviewAndReachReadonlyPostcheck()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(OldHorseId);
        var oldEntryId = DeterministicIdGenerator.BuildRaceEntryId(RaceId, OldHorseId);
        await fixture.SeedInterleavedTicketStreamAcrossPageBoundaryAsync(oldEntryId);
        await HorseIdentityMigrationRuntimeModeTests.ReplaceSupportedEventsWithCommandBusSeedAsync(
            fixture, OldHorseId, RaceId, TicketAggregateId, oldEntryId);
        await fixture.ExecuteAsync(fixture.CollectionPath, $$"""
            UPDATE collection_resources
            SET AttributesJson='{"requestedByHorseId":"{{OldHorseId}}","userNote":"preserve"}'
            WHERE Type='Horse' AND Provider='JRA' AND ResourceId='{{OldHorseId}}';
            """);
        var queueOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        queueOptions.Converters.Add(new HorseRacingPrediction.Contracts.Common.Time.JstStoredDateTimeOffsetJsonConverter());
        var queueBody = JsonSerializer.Serialize(new HorseRacingPrediction.CollectionOperations.CollectionPlatform
            .CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "pending happy fixture"), queueOptions);
        await fixture.SeedLocalQueueMessageAsync(queueBody);
        await fixture.ExecuteAsync(fixture.SchedulePath, """
            INSERT INTO prediction_candidates
                (RaceId, Status, FirstQueuedAt, AvailableAt, LeaseToken, LeaseExpiresAt, LastError, UpdatedAt)
            VALUES ('pending-happy-race', 0, '2026-10-14T00:00:00+09:00',
                    '2026-10-14T00:00:00+09:00', NULL, NULL, 'preserve error', '2026-10-14T00:00:00+09:00');
            """);

        var manifest = Manifest();
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
            requestHash, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash, DateTimeOffset.UtcNow)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
        })
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)).Succeeded);

        var sources = new HorseIdentityMigrationConfiguredSources(fixture.EventStorePath, fixture.CollectionPath,
            fixture.SchedulePath, fixture.QueuePath);
        var backup = HorseIdentityMigrationManagedBackup.ForJournal(journal);
        var receipt = await backup.CreateAsync(operationId, sources, journal);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.BackedUp, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow("Local"));
        var preview = await engine.InspectPreviewAsync(manifest, CancellationToken.None);
        Assert.IsTrue(preview.CanStage);
        Assert.IsNotNull(preview.Fingerprint);
        Assert.IsTrue((await journal.SealPendingRequestAsync(operationId, requestHash, preview.Fingerprint!,
            HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sources.PhysicalSourcePaths),
            receipt.ManifestHash, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Applying, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);

        var execution = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            authorizedSourceSetHash: HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sources.PhysicalSourcePaths),
            authorizedBackupManifestHash: receipt.ManifestHash);

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.Completed, execution.Status);
        Assert.IsTrue(execution.CanWrite);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await journal.GetStatusAsync(operationId))!.Status);
        var verifier = new HorseIdentityMigrationPostVerification(backup,
            fixture.EventFlowServices.GetRequiredService<IEventJsonSerializer>(),
            fixture.EventFlowServices.GetRequiredService<IDomainEventFactory>());
        await verifier.VerifyAsync(operationId, sources, journal,
            HorseIdentityMigrationManifestPolicy.Validate(manifest).Mappings, CancellationToken.None);
    }

    [TestMethod]
    public async Task FullPlanFreshnessFailurePrecedesEveryDomainWrite()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var manifest = Manifest();
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        await StageBoundV1Async(journal, operationId, manifest);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow());

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            async _ => await fixture.ExecuteAsync(fixture.EventStorePath,
                "UPDATE Horses SET RegisteredName='concurrent drift' WHERE HorseId='" + OldHorseId + "';"));

        Assert.AreNotEqual(TypedHorseIdentityExecutionStatus.Completed, result.Status);
        await using var connection = new SqliteConnection($"Data Source={fixture.EventStorePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection, "SELECT HorseId FROM Horses;"));
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection, "SELECT AggregateId FROM EventEntity;"),
            "A stale non-ID row must block before the first physical page write.");
        Assert.AreEqual("concurrent drift", await ScalarAsync(connection, "SELECT RegisteredName FROM Horses;"));
    }

    [TestMethod]
    public async Task RootDestinationCollisionBlocksBeforeAnyDomainWrite()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        var manifest = Manifest();
        var destination = HorseIdentityMigrationManifestPolicy.Validate(manifest).Mappings.Single().DestinationHorseId;
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, $"""
            INSERT INTO Horses (HorseId, RegisteredName, NormalizedName, Aliases)
            VALUES ('{destination}', 'destination root', 'destinationroot', '[]');
            """);
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        await StageBoundV1Async(journal, operationId, manifest);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow());

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None);

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.RejectedBeforeWrite, result.Status);
        Assert.IsFalse(result.CanWrite);
        await using var connection = new SqliteConnection($"Data Source={fixture.EventStorePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        Assert.AreEqual(2L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM Horses;")));
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection,
            "SELECT AggregateId FROM EventEntity WHERE AggregateName='HorseAggregate';"));
    }

    [TestMethod]
    public async Task OwnedExecutionRollsBackFailedPageAndDoesNotAutomaticallyResumePartialOperation()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var manifest = Manifest();
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        await StageBoundV1Async(journal, operationId, manifest);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow());

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            token => fixture.ExecuteAsync(fixture.EventStorePath, """
                CREATE TRIGGER fail_event_page BEFORE UPDATE ON EventEntity
                BEGIN SELECT RAISE(ABORT, 'injected page failure'); END;
                """));

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.PartialUnverified, result.Status);
        Assert.IsFalse(result.CanWrite);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.PartialUnverified,
            (await journal.GetStatusAsync(operationId))!.Status);
        await using var connection = new SqliteConnection($"Data Source={fixture.EventStorePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection, "SELECT HorseId FROM Horses;"));
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection, "SELECT AggregateId FROM EventEntity;"),
            "The failed page transaction must roll back all row changes.");

        var retry = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None);
        Assert.AreEqual(TypedHorseIdentityExecutionStatus.NotOwned, retry.Status,
            "A partial journal is not an implicit resume request.");
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection, "SELECT AggregateId FROM EventEntity;"));
    }

    [TestMethod]
    public async Task OwnedExecutionMarksVerifiedEarlierPagePartialWhenLaterPageCannotCommit()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var manifest = Manifest();
        var destination = HorseIdentityMigrationManifestPolicy.Validate(manifest).Mappings.Single().DestinationHorseId;
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        await StageBoundV1Async(journal, operationId, manifest);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow());

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            token => fixture.ExecuteAsync(fixture.EventStorePath, """
                CREATE TRIGGER fail_horse_page BEFORE UPDATE ON Horses
                BEGIN SELECT RAISE(ABORT, 'injected later-page failure'); END;
                """));

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.PartialUnverified, result.Status);
        Assert.IsFalse(result.CanWrite);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.PartialUnverified,
            (await journal.GetStatusAsync(operationId))!.Status);
        await using var connection = new SqliteConnection($"Data Source={fixture.EventStorePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        Assert.AreEqual(destination, await ScalarAsync(connection, "SELECT AggregateId FROM EventEntity;"),
            "The first verified page remains visible and is represented by a partial operation.");
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection, "SELECT HorseId FROM Horses;"),
            "The later failing page rolls back without being reported writable.");
    }

    [TestMethod]
    public async Task ExecutionDeadlineAfterPreparedCheckpointsPreventsTheFirstBusinessWrite()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var manifest = Manifest();
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        await StageBoundV1Async(journal, operationId, manifest);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow());
        var clock = new AdvancingTimeProvider(DateTimeOffset.UtcNow);
        var before = await fixture.SourceSnapshotAsync();

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            _ => { clock.Advance(TimeSpan.FromMinutes(31)); return Task.CompletedTask; },
            timeProvider: clock);

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.PartialUnverified, result.Status);
        Assert.IsFalse(result.CanWrite);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.PartialUnverified,
            (await journal.GetStatusAsync(operationId))!.Status);
        Assert.AreEqual(before, await fixture.SourceSnapshotAsync(),
            "Expired work after required-page registration must not start a source write.");
    }

    [TestMethod]
    public async Task ExecutionDeadlineAfterCommitRecordsPartialAndNeverReportsSuccess()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var manifest = Manifest();
        var destination = HorseIdentityMigrationManifestPolicy.Validate(manifest).Mappings.Single().DestinationHorseId;
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        await StageBoundV1Async(journal, operationId, manifest);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow());
        var clock = new AdvancingTimeProvider(DateTimeOffset.UtcNow);

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            afterPageCommit: _ => { clock.Advance(TimeSpan.FromMinutes(31)); return Task.CompletedTask; },
            timeProvider: clock);

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.PartialUnverified, result.Status);
        Assert.IsFalse(result.CanWrite);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.PartialUnverified,
            (await journal.GetStatusAsync(operationId))!.Status);
        await using var connection = new SqliteConnection($"Data Source={fixture.EventStorePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        Assert.AreEqual(destination, await ScalarAsync(connection, "SELECT AggregateId FROM EventEntity;"),
            "The committed page remains visible but is not falsely reported as completed.");
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection, "SELECT HorseId FROM Horses;"),
            "Expired execution does not start the next page.");
    }

    [TestMethod]
    public async Task TicketCreatedPointReadConsumesTheSharedRowBudget()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var entryId = DeterministicIdGenerator.BuildRaceEntryId(RaceId, OldHorseId);
        await fixture.SeedInterleavedTicketStreamAcrossPageBoundaryAsync(entryId);
        var reader = fixture.CreateReadersWithEventFlow().CreateReferenceReaderForExecution();
        var budget = new TypedHorseIdentityExecutionBudget();
        budget.AddPreviouslyScanned(999_999, 0);
        await using var connection = new SqliteConnection(
            $"Data Source={fixture.EventStorePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        var markPage = new TypedHorseIdentityPageEvidence(MigrationStoreSurface.EventStore, "EventEntity",
            1, "before", "after", "128", "end");

        var exceeded = false;
        try
        {
            await reader.ReadAndTransformExecutionPageAsync(connection, markPage, [Mapping()],
                CancellationToken.None, budget);
        }
        catch (InvalidDataException)
        {
            exceeded = true;
        }
        Assert.IsTrue(exceeded, "The sequence-1 context point read must consume the shared row limit.");
    }

    [TestMethod]
    public async Task OversizedTicketCreatedDataDriftBlocksBeforeIdentityWrites()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedInterleavedTicketStreamAcrossPageBoundaryAsync(
            DeterministicIdGenerator.BuildRaceEntryId(RaceId, OldHorseId));
        var manifest = Manifest();
        var journal = CreateJournal(fixture);
        var operationId = Guid.NewGuid();
        await StageBoundV1Async(journal, operationId, manifest);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        var engine = new TypedHorseIdentityStoreEngine(fixture.CreateReadersWithEventFlow());

        var result = await engine.ExecuteOwnedAsync(operationId, manifest, journal, CancellationToken.None,
            token => fixture.ExecuteAsync(fixture.EventStorePath, """
                UPDATE EventEntity SET Data=CAST(zeroblob(65537) AS TEXT)
                WHERE AggregateName='PredictionTicketAggregate' AND AggregateSequenceNumber=1;
                """));

        Assert.AreEqual(TypedHorseIdentityExecutionStatus.PartialUnverified, result.Status);
        Assert.IsFalse(result.CanWrite);
        await using var connection = new SqliteConnection(
            $"Data Source={fixture.EventStorePath};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection, "SELECT HorseId FROM Horses;"));
        Assert.AreEqual(OldHorseId, await ScalarAsync(connection,
            "SELECT AggregateId FROM EventEntity WHERE AggregateName='HorseAggregate';"));
        Assert.AreEqual(65537L, Convert.ToInt64(await ScalarAsync(connection,
            "SELECT length(CAST(Data AS BLOB)) FROM EventEntity WHERE AggregateName='PredictionTicketAggregate' AND AggregateSequenceNumber=1;")));
    }

    private static HorseIdentityMigrationManifest Manifest() => new(
        [new HorseIdentityMigrationManifestEntry(OldHorseId, "jra-horse:2024100828", HypothesisAccepted: true)]);

    private static HorseIdentityMigrationJournal CreateJournal(
        HorseIdentityMigrationStoreEngineTests.StoreFixture fixture) => new(
        Path.Combine(fixture.Root, "migration-journal"),
        [fixture.EventStorePath, fixture.CollectionPath, fixture.SchedulePath, fixture.QueuePath]);

    private static async Task StageBoundV1Async(HorseIdentityMigrationJournal journal, Guid operationId,
        HorseIdentityMigrationManifest manifest)
    {
        Assert.IsTrue((await journal.StageAsync(operationId, manifest, DateTimeOffset.UtcNow)).Succeeded);
        var fingerprint = new string('A', 64);
        Assert.IsTrue((await journal.BindStandaloneRequestAsync(operationId, manifest, fingerprint,
            DateTimeOffset.UtcNow)).Succeeded);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static HorseIdentityMigrationMapping Mapping()
    {
        var manifest = new HorseIdentityMigrationManifest(
            [new HorseIdentityMigrationManifestEntry(OldHorseId, "jra-horse:2024100828", HypothesisAccepted: true)]);
        var result = HorseIdentityMigrationManifestPolicy.Validate(manifest);
        Assert.IsTrue(result.IsValid);
        return result.Mappings.Single();
    }

    private sealed class AdvancingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan amount) => _now += amount;
    }
}
