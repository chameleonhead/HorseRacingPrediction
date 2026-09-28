using System.Text.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Tests;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionDispatchStarvationReproductionTests
{
    [TestMethod]
    [DataRow("stale-generation")]
    [DataRow("terminal-task")]
    public async Task WakeOnlyReservation_SkipsStaleCandidateAndAcquiresBackground(string invalidCandidate)
    {
        var root = Path.Combine(Path.GetTempPath(), "collection-starvation-repro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventDatabase = Path.Combine(root, "events.db");
        var (app, http) = await TestApplicationFactory.CreateAsync($"Data Source={eventDatabase}");
        try
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var store = app.Services.GetRequiredService<CollectionPlatformStore>();
            var now = DateTimeOffset.UtcNow.AddMinutes(-5);
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);

            var invalidReceipt = await store.RequestAsync(
                new(CollectionResourceType.Race, "JRA", $"INVALID-{invalidCandidate}"), definition, 1,
                CollectionReason.Initial, now, CollectionLane.Background, 100);
            var backgroundReceipt = await store.RequestAsync(
                new(CollectionResourceType.Race, "JRA", "VALID-BACKGROUND"), definition, 1,
                CollectionReason.Backfill, now, CollectionLane.Background, 10);

            var databasePath = Path.Combine(Path.GetFullPath(eventDatabase) + ".collection", "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var invalidTask = await db.Tasks.SingleAsync(x => x.TaskId == invalidReceipt.TaskId);
                var invalidOutbox = await db.DispatchOutbox.SingleAsync(x => x.TaskId == invalidReceipt.TaskId);
                if (invalidCandidate == "stale-generation")
                {
                    Assert.AreEqual(CollectionTaskStatus.Ready, invalidTask.Status);
                    invalidOutbox.DispatchGeneration = invalidTask.DispatchGeneration + 1;
                }
                else
                {
                    invalidTask.Status = CollectionTaskStatus.DeadLetter;
                    invalidOutbox.DispatchGeneration = invalidTask.DispatchGeneration;
                }
                await db.SaveChangesAsync();
            }

            var queue = new LocalWakeQueue();
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 1,
                OutboxReservationSeconds = 45,
                AggregationDelayMilliseconds = 0
            });
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue, queueOptions,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var worker = new CollectionPlatformWorkerClient(http,
                new CollectionDefinitionHandlerRegistry(Array.Empty<ICollectionDefinitionHandler>()));

            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            Assert.AreEqual(1, queue.Wakes.Count, "The first real dispatch cycle must send exactly one wake.");
            var wake = queue.Wakes.Single();
            var selectedOutbox = await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId);
            Assert.AreEqual(backgroundReceipt.TaskId, selectedOutbox.TaskId,
                "The stale higher-priority outbox must not hide the valid Background task.");
            for (var cycle = 0; cycle < 2; cycle++)
            {
                await dispatcher.DispatchOnceAsync(CancellationToken.None);
                Assert.AreEqual(1, queue.Wakes.Count,
                    "The sole valid Background reservation must not be emitted more than once before acquisition.");
            }
            var delivery = queue.ReceiveNext();

            var acquire = await worker.AcquireNextAsync(wake, delivery.MessageId, CancellationToken.None);
            Assert.AreEqual(CollectionExecutionAcquireStatus.Acquired, acquire.Status,
                "The valid Background task must be acquired by the real HTTP boundary.");
            var lambdaResponse = await CollectionLambdaInvocation.ExecuteWakeAsync(
                CreateSqsWakeEvent(delivery), worker,
                cancellationToken: CancellationToken.None,
                executeGroup: static (_, _, _) => Task.CompletedTask);
            Assert.AreEqual(0, lambdaResponse.BatchItemFailures.Count,
                "The Lambda invocation must acknowledge the typed acquire result.");
            queue.ApplyBatchResponse(lambdaResponse);
            Assert.AreEqual(0, queue.VisibleCount,
                "A typed NoWork or successfully acquired batch must acknowledge its local queue message.");

            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                Assert.IsTrue(await db.ExecutionLeases.AnyAsync(x => x.DispatchEnvelopeId == wake.DispatchEnvelopeId),
                    "The bounded poll window must create an execution lease for the selected valid wake.");
                var preservedInvalidTask = await db.Tasks.SingleAsync(x => x.TaskId == invalidReceipt.TaskId);
                var preservedInvalidOutbox = await db.DispatchOutbox.SingleAsync(x => x.TaskId == invalidReceipt.TaskId);
                Assert.AreEqual(invalidCandidate == "stale-generation" ? CollectionTaskStatus.Ready : CollectionTaskStatus.DeadLetter,
                    preservedInvalidTask.Status, "Selection must not repair or delete the invalid task.");
                Assert.IsNull(preservedInvalidOutbox.ReservationToken,
                    "The stale outbox must remain unreserved while the valid Background task proceeds.");
            }
        }
        finally
        {
            http.Dispose();
            await app.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("terminal-task")]
    [DataRow("generation")]
    public async Task ReservationRechecksCandidateMutationAndContinuesToNextTask(string mutation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-reservation-race", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var now = DateTimeOffset.UtcNow.AddMinutes(-2);
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var changing = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", $"CHANGING-{mutation}"),
                definition, 1, CollectionReason.Initial, now, CollectionLane.Background, 100);
            var valid = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", $"VALID-{mutation}"),
                definition, 1, CollectionReason.Backfill, now, CollectionLane.Background, 10);
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            var queue = new WakeCaptureQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 1,
                    MaxInFlightEnvelopes = 1,
                    OutboxReservationSeconds = 45,
                    AggregationDelayMilliseconds = 0
                }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var changed = false;
            dispatcher.BeforeReservationAsync = async (group, cancellationToken) =>
            {
                if (changed || group.All(x => x.Notification.TaskId != changing.TaskId)) return;
                await using var db = new CollectionPlatformDbContext(dbOptions);
                var task = await db.Tasks.SingleAsync(x => x.TaskId == changing.TaskId, cancellationToken);
                if (mutation == "terminal-task") task.Status = CollectionTaskStatus.DeadLetter;
                else task.DispatchGeneration++;
                await db.SaveChangesAsync(cancellationToken);
                changed = true;
            };

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            Assert.IsTrue(changed, "The deterministic seam must mutate the selected task before reservation.");
            Assert.AreEqual(1, queue.Wakes.Count,
                "A transaction-time rejection must not consume the cycle's only dispatch grant.");
            var reserved = await LoadOutboxForWakeAsync(dbOptions, queue.Wakes[0].DispatchEnvelopeId);
            Assert.AreEqual(valid.TaskId, reserved.TaskId,
                "The bounded scan must continue from the invalidated candidate to valid work.");
            await using var verify = new CollectionPlatformDbContext(dbOptions);
            var invalidOutbox = await verify.DispatchOutbox.SingleAsync(x => x.TaskId == changing.TaskId);
            Assert.IsNull(invalidOutbox.ReservationToken,
                "The changed candidate must not receive a stale reservation token.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PendingSelection_LeavesMissingAndDuplicateOutboxAnomaliesUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-outbox-anomalies", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var now = DateTimeOffset.UtcNow;
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var missing = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "MISSING"), definition,
                1, CollectionReason.Initial, now);
            var duplicate = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "DUPLICATE"), definition,
                1, CollectionReason.Initial, now);
            var valid = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "VALID"), definition,
                1, CollectionReason.Initial, now);
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var missingRow = await db.DispatchOutbox.SingleAsync(x => x.TaskId == missing.TaskId);
                db.DispatchOutbox.Remove(missingRow);
                var duplicateRow = await db.DispatchOutbox.SingleAsync(x => x.TaskId == duplicate.TaskId);
                db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                {
                    OutboxId = Guid.NewGuid(),
                    TaskId = duplicateRow.TaskId,
                    DispatchGeneration = duplicateRow.DispatchGeneration,
                    AvailableAt = duplicateRow.AvailableAt,
                    CreatedAt = duplicateRow.CreatedAt
                });
                await db.SaveChangesAsync();
            }

            var pending = await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10);

            Assert.AreEqual(valid.TaskId, pending.Single().Notification.TaskId,
                "Only a task with exactly one current-generation outbox row is dispatchable.");
            await using var after = new CollectionPlatformDbContext(dbOptions);
            Assert.AreEqual(0, await after.DispatchOutbox.CountAsync(x => x.TaskId == missing.TaskId),
                "Selection must not synthesize a replacement outbox row.");
            Assert.AreEqual(2, await after.DispatchOutbox.CountAsync(x => x.TaskId == duplicate.TaskId),
                "Selection must preserve every duplicate row for separately reviewed repair.");
            Assert.AreEqual(CollectionTaskStatus.Ready,
                (await after.Tasks.SingleAsync(x => x.TaskId == missing.TaskId)).Status,
                "The missing-outbox task must remain untouched.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CapacityOne_IgnoresAnActiveReservationForAStaleGeneration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-stale-capacity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var now = DateTimeOffset.UtcNow.AddMinutes(-2);
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var stale = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "STALE-CAPACITY"), definition,
                1, CollectionReason.Initial, now, CollectionLane.Background, 100);
            var valid = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "VALID-CAPACITY"), definition,
                1, CollectionReason.Initial, now, CollectionLane.Background, 10);
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var task = await db.Tasks.SingleAsync(x => x.TaskId == stale.TaskId);
                var outbox = await db.DispatchOutbox.SingleAsync(x => x.TaskId == stale.TaskId);
                task.DispatchGeneration++;
                outbox.ReservationToken = "stale-generation-reservation";
                outbox.ReservedUntilUnixMilliseconds = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds();
                outbox.EnvelopeId = Guid.NewGuid();
                outbox.WakeId = Guid.NewGuid();
                await db.SaveChangesAsync();
            }
            var queue = new WakeCaptureQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 1,
                    MaxInFlightEnvelopes = 1,
                    AggregationDelayMilliseconds = 0
                }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            Assert.AreEqual(1, queue.Wakes.Count,
                "A stale active reservation must not consume the only eligible dispatch slot.");
            Assert.AreEqual(valid.TaskId,
                (await LoadOutboxForWakeAsync(dbOptions, queue.Wakes[0].DispatchEnvelopeId)).TaskId,
                "The valid current-generation task must get capacity first.");
            await using var verify = new CollectionPlatformDbContext(dbOptions);
            var preserved = await verify.DispatchOutbox.SingleAsync(x => x.TaskId == stale.TaskId);
            Assert.AreEqual("stale-generation-reservation", preserved.ReservationToken,
                "Capacity accounting must leave stale reservation evidence untouched.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PendingSelection_ExcludesTasksCoveredByAnActiveRepairHold()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-active-hold", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var now = DateTimeOffset.UtcNow;
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var raceId = $"race-{Guid.NewGuid():D}";
            var receipt = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", raceId), definition,
                1, CollectionReason.Initial, now);
            await store.HoldRaceForRepairAsync(raceId, Guid.NewGuid().ToString(), 0,
                "dispatch eligibility test", now);

            Assert.IsEmpty(await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10),
                "An active canonical repair hold must exclude its Ready task from dispatch.");
            Assert.AreEqual(CollectionTaskStatus.Ready,
                (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId).Status,
                "Eligibility filtering must leave the held task unchanged.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ReservationRelease_RequiresExactWakeAndTokenAndPreservesLiveLease()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-release-fencing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var now = DateTimeOffset.UtcNow;
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var receipt = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "RELEASE"), definition,
                1, CollectionReason.Initial, now);
            var pending = (await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10)).Single();
            var wakeId = Guid.NewGuid();
            var envelopeId = Guid.NewGuid();
            var token = Guid.NewGuid().ToString("N");
            Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending.OutboxId], token,
                envelopeId, wakeId, now, TimeSpan.FromMinutes(1), 1));

            Assert.AreEqual(CollectionReservationReleaseOutcome.AlreadyReleasedOrChanged,
                await store.ReleaseDispatchReservationAsync(wakeId, envelopeId, "different-token", now.AddSeconds(1)));
            Assert.AreEqual(CollectionReservationReleaseOutcome.AlreadyReleasedOrChanged,
                await store.ReleaseDispatchReservationAsync(Guid.NewGuid(), envelopeId, token, now.AddSeconds(1)));
            await using (var db = new CollectionPlatformDbContext(new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                             .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False")
                             .Options))
            {
                var row = await db.DispatchOutbox.SingleAsync(x => x.OutboxId == pending.OutboxId);
                Assert.AreEqual(token, row.ReservationToken);
                Assert.AreEqual(wakeId, row.WakeId);
                db.ExecutionLeases.Add(new CollectionExecutionLeaseEntity
                {
                    ExecutionBatchId = Guid.NewGuid(),
                    DispatchEnvelopeId = envelopeId,
                    WakeId = wakeId,
                    ReservationToken = token,
                    LeaseToken = "active-lease",
                    Status = "Running",
                    LeaseExpiresAt = now.AddMinutes(1),
                    CreatedAt = now
                });
                await db.SaveChangesAsync();
            }

            Assert.AreEqual(CollectionReservationReleaseOutcome.SkippedActiveLease,
                await store.ReleaseDispatchReservationAsync(wakeId, envelopeId, token, now.AddSeconds(1)));
            await using (var verify = new CollectionPlatformDbContext(new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                             .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False")
                             .Options))
            {
                var row = await verify.DispatchOutbox.SingleAsync(x => x.OutboxId == pending.OutboxId);
                Assert.AreEqual(token, row.ReservationToken, "A live worker lease must retain the reservation identity.");
                Assert.AreEqual(wakeId, row.WakeId);
                Assert.AreEqual(CollectionTaskStatus.Ready,
                    (await verify.Tasks.SingleAsync(x => x.TaskId == receipt.TaskId)).Status,
                    "Reservation release must never change task state.");
            }

            await using (var db = new CollectionPlatformDbContext(new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                             .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False")
                             .Options))
            {
                var lease = await db.ExecutionLeases.SingleAsync(x => x.DispatchEnvelopeId == envelopeId);
                db.ExecutionLeases.Remove(lease);
                var task = await db.Tasks.SingleAsync(x => x.TaskId == receipt.TaskId);
                task.DispatchGeneration++;
                await db.SaveChangesAsync();
            }
            Assert.AreEqual(CollectionReservationReleaseOutcome.SkippedStaleGeneration,
                await store.ReleaseDispatchReservationAsync(wakeId, envelopeId, token, now.AddSeconds(2)));
            await using (var verifyStale = new CollectionPlatformDbContext(new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                             .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False")
                             .Options))
            {
                var row = await verifyStale.DispatchOutbox.SingleAsync(x => x.OutboxId == pending.OutboxId);
                Assert.AreEqual(token, row.ReservationToken, "A stale-generation reservation must remain preserved.");
                Assert.AreEqual(wakeId, row.WakeId);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ReservationRelease_ClearsOnlyTheExactCurrentReservation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-release-exact", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var now = DateTimeOffset.UtcNow;
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var receipt = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "RELEASE-EXACT"), definition,
                1, CollectionReason.Initial, now);
            var pending = (await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10)).Single();
            var wakeId = Guid.NewGuid();
            var envelopeId = Guid.NewGuid();
            var token = Guid.NewGuid().ToString("N");
            Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending.OutboxId], token,
                envelopeId, wakeId, now, TimeSpan.FromMinutes(1), 1));

            Assert.AreEqual(CollectionReservationReleaseOutcome.Released,
                await store.ReleaseDispatchReservationAsync(wakeId, envelopeId, token, now.AddSeconds(1)));
            var databasePath = Path.Combine(directory, "collection-platform.db");
            await using var db = new CollectionPlatformDbContext(new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False").Options);
            var row = await db.DispatchOutbox.SingleAsync(x => x.OutboxId == pending.OutboxId);
            Assert.IsNull(row.ReservationToken);
            Assert.IsNull(row.ReservedUntilUnixMilliseconds);
            Assert.IsNull(row.EnvelopeId);
            Assert.IsNull(row.WakeId);
            Assert.IsNull(row.QueueMessageId);
            Assert.AreEqual(CollectionTaskStatus.Ready,
                (await db.Tasks.SingleAsync(x => x.TaskId == receipt.TaskId)).Status,
                "A safe reservation release must not mutate task state.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Schema19_AddsWakeIdentityWithoutChangingExistingReservationRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-schema-19", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var store = new CollectionPlatformStore(options);
            var now = DateTimeOffset.UtcNow;
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var receipt = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "SCHEMA-19"), definition,
                1, CollectionReason.Initial, now);
            var pending = (await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10)).Single();
            var envelopeId = Guid.NewGuid();
            var wakeId = Guid.NewGuid();
            const string token = "schema-18-reservation";
            Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending.OutboxId], token,
                envelopeId, wakeId, now, TimeSpan.FromMinutes(1), 1));
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE collection_task_outbox DROP COLUMN WakeId; " +
                    "DELETE FROM collection_schema_history WHERE version = 19;");

            var migrated = new CollectionPlatformStore(options);
            await migrated.GetPendingDispatchesAsync(now.AddSeconds(1), 10);

            await using var verify = new CollectionPlatformDbContext(dbOptions);
            Assert.AreEqual(1, await verify.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM pragma_table_info('collection_task_outbox') WHERE name = 'WakeId'")
                .SingleAsync(), "Opening a schema-18 database must apply the nullable v19 column.");
            var preserved = await verify.DispatchOutbox.SingleAsync(x => x.OutboxId == pending.OutboxId);
            Assert.AreEqual(receipt.TaskId, preserved.TaskId);
            Assert.AreEqual(token, preserved.ReservationToken,
                "Schema upgrade must preserve existing reservation state for ordinary expiry/reacquisition.");
            Assert.AreEqual(envelopeId, preserved.EnvelopeId);
            Assert.IsNull(preserved.WakeId,
                "A pre-v19 reservation has no verifiable wake identity and must not be assigned one during migration.");
            Assert.AreEqual(1, await verify.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM collection_dispatcher_fairness_state WHERE StateId = 1")
                .SingleAsync(), "Schema migration initializes exactly one dispatcher fairness record.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RaceOddsBypassesAggregationDelay_WhileOtherDefinitionsRemainDelayed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-race-odds-delay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-odds"), "Race odds", CollectionResourceType.RaceOdds,
                1, "initial", false);
            await store.RegisterDefinitionAsync(new("race-result"), "Race result", CollectionResourceType.RaceResult,
                1, "initial", false);
            var requestedAt = DateTimeOffset.UtcNow;
            var odds = await store.RequestAsync(new(CollectionResourceType.RaceOdds, "JRA", "ODDS"),
                new("race-odds"), 1, CollectionReason.Initial, requestedAt, CollectionLane.Background, 10);
            var result = await store.RequestAsync(new(CollectionResourceType.RaceResult, "JRA", "RESULT"),
                new("race-result"), 1, CollectionReason.Initial, requestedAt, CollectionLane.Background, 10);
            var queue = new WakeCaptureQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 1,
                    MaxInFlightEnvelopes = 1,
                    AggregationDelayMilliseconds = 60_000
                }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var wake = queue.Wakes.Single();
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            var dispatched = await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId);
            Assert.AreEqual(odds.TaskId, dispatched.TaskId,
                "race-odds must dispatch immediately despite the configured aggregation delay.");
            await using var db = new CollectionPlatformDbContext(dbOptions);
            var delayed = await db.DispatchOutbox.SingleAsync(x => x.TaskId == result.TaskId);
            Assert.IsNull(delayed.ReservationToken,
                "A similarly aged non-race-odds definition must remain delayed and unreserved.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LaneFairnessAndScanCursorPersistAcrossRestartsAtSingleInFlightCapacity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-persisted-fairness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var store = new CollectionPlatformStore(options);
            var definition = new CollectionDefinitionId("race-detail");
            var requestedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var lanes = new[] { CollectionLane.Realtime, CollectionLane.Normal, CollectionLane.Background };
            for (var index = 0; index < 8; index++)
                foreach (var lane in lanes)
                    await store.RequestAsync(new(CollectionResourceType.Race, "JRA", $"FAIR-{lane}-{index}"),
                        definition, 1, CollectionReason.Initial, requestedAt, lane, 10);

            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var persisted = await db.DispatcherFairnessStates.SingleAsync(x => x.StateId == 1);
                persisted.ConsecutiveRealtime = 4;
                persisted.LastNonRealtimeLane = CollectionLane.Normal.ToString();
                await db.SaveChangesAsync();
            }

            var queue = new WakeCaptureQueue();
            var dispatchedLanes = new List<CollectionLane>();
            var now = DateTimeOffset.UtcNow;
            for (var index = 0; index < 12; index++)
            {
                // New store/dispatcher instances model a process restart between every granted wake.
                store = new CollectionPlatformStore(options);
                var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                    Options.Create(new CollectionQueueOptions
                    {
                        Enabled = true,
                        DispatchBatchSize = 1,
                        EnvelopeMaxTasks = 1,
                        MaxInFlightEnvelopes = 1,
                        OutboxReservationSeconds = 60,
                        AggregationDelayMilliseconds = 0
                    }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
                var previousWakeCount = queue.Wakes.Count;
                await dispatcher.DispatchOnceAsync(CancellationToken.None);
                Assert.AreEqual(previousWakeCount + 1, queue.Wakes.Count,
                    "MaxInFlight=1 must allow one reservation per cycle and continue after its lease completes.");
                var wake = queue.Wakes[^1];
                var outbox = await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId);
                await using (var db = new CollectionPlatformDbContext(dbOptions))
                {
                    var task = await db.Tasks.SingleAsync(x => x.TaskId == outbox.TaskId);
                    dispatchedLanes.Add(task.Lane);
                    Assert.AreEqual(index, (int)await db.Database.SqlQueryRaw<long>(
                        "SELECT ReservationSequence AS Value FROM collection_dispatcher_fairness_state WHERE StateId = 1")
                        .SingleAsync(), "Sending a wake alone must not advance persisted fairness or the scan cursor.");
                    var acquired = await store.AcquireNextExecutionAsync(wake, $"fair-{index}", now,
                        TimeSpan.FromSeconds(45));
                    Assert.AreEqual(CollectionExecutionAcquireStatus.Acquired, acquired.Status);
                    var stateAfterRestart = await new CollectionPlatformStore(options).GetLaneDispatchStateAsync();
                    Assert.AreEqual(index + 1, (int)await db.Database.SqlQueryRaw<long>(
                        "SELECT ReservationSequence AS Value FROM collection_dispatcher_fairness_state WHERE StateId = 1")
                        .SingleAsync());
                    Assert.AreEqual(outbox.OutboxId, stateAfterRestart.ScanOutboxId,
                        "The keyset cursor must be written in the same transaction as acquisition.");
                    Assert.AreEqual(task.Lane, Enum.Parse<CollectionLane>(
                        (await db.DispatcherFairnessStates.SingleAsync(x => x.StateId == 1)).LastGrantedLane!));
                    Assert.IsTrue(await store.CompleteExecutionAsync(acquired.ExecutionBatchId!.Value,
                        acquired.LeaseToken!, now.AddSeconds(1)));
                }
                now = now.AddSeconds(2);
            }

            Assert.IsTrue(dispatchedLanes.Contains(CollectionLane.Normal),
                $"Normal must receive service: {string.Join(',', dispatchedLanes)}");
            Assert.IsTrue(dispatchedLanes.Contains(CollectionLane.Background),
                $"Background must receive service: {string.Join(',', dispatchedLanes)}");
            var realtimeBurst = 0;
            foreach (var lane in dispatchedLanes)
            {
                realtimeBurst = lane == CollectionLane.Realtime ? realtimeBurst + 1 : 0;
                Assert.IsTrue(realtimeBurst <= 4,
                    $"Realtime must yield after four acquired envelopes: {string.Join(',', dispatchedLanes)}");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<CollectionDispatchOutboxEntity> LoadOutboxForWakeAsync(
        DbContextOptions<CollectionPlatformDbContext> dbOptions, Guid envelopeId)
    {
        await using var db = new CollectionPlatformDbContext(dbOptions);
        return await db.DispatchOutbox.SingleAsync(x => x.EnvelopeId == envelopeId);
    }

    private static string CreateSqsWakeEvent(LocalWakeQueue.LocalMessage message)
    {
        return JsonSerializer.Serialize(new
        {
            Records = new[] { new { messageId = message.MessageId, body = message.Body } }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private sealed class LocalWakeQueue : ICollectionPlatformTaskQueue
    {
        private readonly List<LocalMessage> _messages = [];
        public List<CollectionWakeSignal> Wakes { get; } = [];
        public int VisibleCount => _messages.Count;

        public sealed record LocalMessage(string MessageId, string Body);

        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new AssertFailedException("Only wake-only messages are expected.");

        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken)
        {
            Wakes.Add(wake);
            var messageId = $"local-message-{Wakes.Count}";
            _messages.Add(new LocalMessage(messageId,
                JsonSerializer.Serialize(wake, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            return Task.FromResult(new CollectionQueueSendReceipt(messageId));
        }

        public LocalMessage ReceiveNext() => _messages.Single();

        public void ApplyBatchResponse(CollectionLambdaBatchResponse response)
        {
            var failedIds = response.BatchItemFailures.Select(x => x.ItemIdentifier).ToHashSet(StringComparer.Ordinal);
            _messages.RemoveAll(x => !failedIds.Contains(x.MessageId));
        }
    }

    private sealed class WakeCaptureQueue : ICollectionPlatformTaskQueue
    {
        public List<CollectionWakeSignal> Wakes { get; } = [];

        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new AssertFailedException("Only wake-only messages are expected.");

        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken)
        {
            Wakes.Add(wake);
            return Task.FromResult(new CollectionQueueSendReceipt($"wake-{Wakes.Count}"));
        }
    }
}
