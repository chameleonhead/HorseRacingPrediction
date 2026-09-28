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
    [DataRow("hold")]
    [DataRow("duplicate-outbox")]
    [DataRow("available-at")]
    public async Task AcquireRevalidatesWholeReservedEnvelopeBeforeAnyMutation(string mutation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-acquire-envelope-recheck", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var store = new CollectionPlatformStore(options);
            var now = DateTimeOffset.UtcNow.AddMinutes(-5);
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var firstResource = new ResourceKey(CollectionResourceType.Race, "JRA", "20260926:Nakayama:5");
            var secondResource = new ResourceKey(CollectionResourceType.Race, "JRA", "20260926:Nakayama:6");
            var first = await store.RequestAsync(firstResource, definition, 1, CollectionReason.Initial,
                now, CollectionLane.Background, 10);
            var second = await store.RequestAsync(secondResource, definition, 1, CollectionReason.Initial,
                now, CollectionLane.Background, 9);
            var later = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "20260926:Nakayama:7"),
                definition, 1, CollectionReason.Backfill, now, CollectionLane.Background, 1);
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            var pending = await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10);
            var reservedIds = pending.Where(x => x.Notification.TaskId == first.TaskId
                || x.Notification.TaskId == second.TaskId).Select(x => x.OutboxId).ToArray();
            var wakeId = Guid.NewGuid();
            var envelopeId = Guid.NewGuid();
            const string token = "whole-envelope-reservation";
            Assert.AreEqual(2, reservedIds.Length);
            Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync(reservedIds, token, envelopeId,
                wakeId, now, TimeSpan.FromMinutes(1), 1));

            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var firstTask = await db.Tasks.SingleAsync(x => x.TaskId == first.TaskId);
                var firstOutbox = await db.DispatchOutbox.SingleAsync(x => x.TaskId == first.TaskId);
                if (mutation == "hold")
                {
                    db.RaceRepairHolds.Add(new RaceRepairHoldEntity
                    {
                        RaceId = DeterministicIdGenerator.TryBuildRaceIdFromResource(firstResource.Id)!,
                        Generation = 1,
                        OperationId = Guid.NewGuid().ToString(),
                        Reason = "recheck reserved envelope",
                        CreatedAt = now.AddSeconds(2)
                    });
                    await db.SaveChangesAsync();
                }
                else if (mutation == "duplicate-outbox")
                {
                    db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                    {
                        OutboxId = Guid.NewGuid(),
                        TaskId = firstOutbox.TaskId,
                        DispatchGeneration = firstOutbox.DispatchGeneration,
                        AvailableAt = firstOutbox.AvailableAt,
                        CreatedAt = firstOutbox.CreatedAt,
                        ReservationToken = token,
                        ReservedUntilUnixMilliseconds = firstOutbox.ReservedUntilUnixMilliseconds,
                        EnvelopeId = envelopeId,
                        WakeId = wakeId
                    });
                    await db.SaveChangesAsync();
                }
                else
                {
                    firstTask.AvailableAt = now.AddMinutes(1);
                    firstOutbox.AvailableAt = now.AddMinutes(1);
                    await db.SaveChangesAsync();
                }
            }

            var result = await store.AcquireNextExecutionAsync(new(wakeId, envelopeId, token), "recheck-message",
                now.AddSeconds(3), TimeSpan.FromSeconds(45));

            Assert.AreEqual(CollectionExecutionAcquireStatus.NoWork, result.Status,
                "An invalid row must reject the complete envelope before any dispatch mutation.");
            await using (var verify = new CollectionPlatformDbContext(dbOptions))
            {
                Assert.AreEqual(0, await verify.ExecutionLeases.CountAsync(x => x.DispatchEnvelopeId == envelopeId));
                Assert.IsTrue(await verify.DispatchOutbox.Where(x => x.TaskId == first.TaskId || x.TaskId == second.TaskId)
                    .AllAsync(x => x.DispatchedAt == null), "No member of a rejected envelope may be marked dispatched.");
                Assert.IsTrue(await verify.DispatchOutbox.Where(x => x.TaskId == first.TaskId || x.TaskId == second.TaskId)
                    .Where(x => reservedIds.Contains(x.OutboxId)).AllAsync(x => x.ReservationToken == token),
                    "A failed acquire retains exact reservations for expiry/re-dispatch; it does not partially clear rows.");
            }
            var laterPending = await store.GetPendingDispatchesAsync(now.AddSeconds(3), 10);
            Assert.IsTrue(laterPending.Any(x => x.Notification.TaskId == later.TaskId),
                "A later eligible task remains pending after the whole-envelope rejection.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void UnclassifiedOrAcquiredResultsAreNeverSafeToRelease()
    {
        Assert.IsFalse(new CollectionExecutionAcquireResult(CollectionExecutionAcquireStatus.NoWork)
            .SafeToReleaseReservation, "A missing reason must fail closed.");
        Assert.IsFalse(new CollectionExecutionAcquireResult(CollectionExecutionAcquireStatus.Acquired)
            .SafeToReleaseReservation, "Acquired results are not NoWork release decisions.");
    }

    [TestMethod]
    [DataRow("invalid-request", CollectionExecutionNoWorkReason.InvalidRequest, false)]
    [DataRow("pipeline-paused", CollectionExecutionNoWorkReason.PipelinePaused, true)]
    [DataRow("active-task-lease", CollectionExecutionNoWorkReason.LeaseConflict, false)]
    [DataRow("active-execution-lease", CollectionExecutionNoWorkReason.LeaseConflict, false)]
    [DataRow("reservation-unavailable", CollectionExecutionNoWorkReason.ReservationUnavailable, false)]
    [DataRow("extra-envelope-row", CollectionExecutionNoWorkReason.ReservationInconsistent, false)]
    [DataRow("duplicate-outbox", CollectionExecutionNoWorkReason.ReservationInconsistent, false)]
    [DataRow("terminal-task", CollectionExecutionNoWorkReason.TaskIneligible, true)]
    [DataRow("repair-hold", CollectionExecutionNoWorkReason.RepairHold, true)]
    [DataRow("resource-unavailable", CollectionExecutionNoWorkReason.ResourceUnavailable, true)]
    [DataRow("invalid-built-envelope", CollectionExecutionNoWorkReason.EnvelopeInvalid, false)]
    public async Task AcquireNoWorkReasonsAreTypedAndReleaseSafetyIsStoreOwned(string scenario,
        CollectionExecutionNoWorkReason expectedReason, bool expectedSafeToRelease)
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-no-work-reason", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var now = DateTimeOffset.UtcNow.AddMinutes(-5);
            var definition = new CollectionDefinitionId("race-detail");
            var resource = new ResourceKey(CollectionResourceType.Race, "JRA", "20260926:Nakayama:5");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial, now,
                CollectionLane.Background, 10);
            var candidate = (await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10)).Single();
            var wakeId = Guid.NewGuid();
            var envelopeId = Guid.NewGuid();
            const string token = "typed-no-work-reservation";
            Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([candidate.OutboxId], token,
                envelopeId, wakeId, now, TimeSpan.FromMinutes(1), 1));
            var wake = new CollectionWakeSignal(wakeId, envelopeId, token);
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False;Default Timeout=30")
                .Options;

            if (scenario == "pipeline-paused")
                await store.SetPausedAsync(true, "typed NoWork test", now.AddSeconds(1));
            else if (scenario is "active-task-lease" or "active-execution-lease" or "terminal-task" or "repair-hold"
                     or "resource-unavailable" or "extra-envelope-row" or "duplicate-outbox")
            {
                await using var db = new CollectionPlatformDbContext(dbOptions);
                var task = await db.Tasks.SingleAsync(x => x.TaskId == receipt.TaskId);
                var outbox = await db.DispatchOutbox.SingleAsync(x => x.TaskId == receipt.TaskId);
                switch (scenario)
                {
                    case "active-task-lease":
                        task.LeaseExpiresAt = now.AddSeconds(30);
                        break;
                    case "active-execution-lease":
                        db.ExecutionLeases.Add(new CollectionExecutionLeaseEntity
                        {
                            ExecutionBatchId = Guid.NewGuid(),
                            DispatchEnvelopeId = envelopeId,
                            WakeId = Guid.NewGuid(),
                            ReservationToken = "different-token",
                            LeaseToken = Guid.NewGuid().ToString("N"),
                            Status = "Running",
                            LeaseExpiresAt = now.AddSeconds(30),
                            CreatedAt = now,
                            QueueMessageId = "active-lease-conflict"
                        });
                        break;
                    case "terminal-task":
                        task.Status = CollectionTaskStatus.DeadLetter;
                        break;
                    case "repair-hold":
                        db.RaceRepairHolds.Add(new RaceRepairHoldEntity
                        {
                            RaceId = DeterministicIdGenerator.TryBuildRaceIdFromResource(resource.Id)!,
                            Generation = 1,
                            OperationId = Guid.NewGuid().ToString("N"),
                            Reason = "typed NoWork test",
                            CreatedAt = now.AddSeconds(1)
                        });
                        break;
                    case "resource-unavailable":
                        task.ResourcePk = long.MaxValue;
                        break;
                    case "extra-envelope-row":
                        db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                        {
                            OutboxId = Guid.NewGuid(),
                            TaskId = outbox.TaskId,
                            DispatchGeneration = outbox.DispatchGeneration,
                            AvailableAt = outbox.AvailableAt,
                            CreatedAt = outbox.CreatedAt,
                            ReservationToken = "conflicting-token",
                            ReservedUntilUnixMilliseconds = outbox.ReservedUntilUnixMilliseconds,
                            EnvelopeId = envelopeId,
                            WakeId = wakeId
                        });
                        break;
                    case "duplicate-outbox":
                        db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                        {
                            OutboxId = Guid.NewGuid(),
                            TaskId = outbox.TaskId,
                            DispatchGeneration = outbox.DispatchGeneration,
                            AvailableAt = outbox.AvailableAt,
                            CreatedAt = outbox.CreatedAt
                        });
                        break;
                }
                await db.SaveChangesAsync();
            }
            else if (scenario == "invalid-built-envelope")
            {
                var acquired = await store.AcquireNextExecutionAsync(wake, "first-attempt", now.AddSeconds(1),
                    TimeSpan.FromSeconds(45));
                Assert.AreEqual(CollectionExecutionAcquireStatus.Acquired, acquired.Status);
                await using var db = new CollectionPlatformDbContext(dbOptions);
                var task = await db.Tasks.SingleAsync(x => x.TaskId == receipt.TaskId);
                task.DispatchGeneration++;
                await db.SaveChangesAsync();
            }

            var requestedWake = scenario == "invalid-request" ? wake with { WakeId = Guid.Empty } : wake;
            var requestedTokenWake = scenario == "reservation-unavailable"
                ? requestedWake with { ReservationToken = "wrong-token" }
                : requestedWake;
            var result = await store.AcquireNextExecutionAsync(requestedTokenWake, "typed-no-work-message",
                now.AddSeconds(2), TimeSpan.FromSeconds(45));

            Assert.AreEqual(CollectionExecutionAcquireStatus.NoWork, result.Status);
            Assert.AreEqual(expectedReason, result.NoWorkReason, $"Unexpected reason for {scenario}.");
            Assert.AreEqual(expectedSafeToRelease, result.SafeToReleaseReservation,
                $"Release disposition must be derived from the reason for {scenario}.");
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

            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var task = await db.Tasks.SingleAsync(x => x.TaskId == receipt.TaskId);
                task.LeaseToken = "other-worker-task-lease";
                task.LeaseExpiresAt = now.AddMinutes(1);
                await db.SaveChangesAsync();
            }
            Assert.AreEqual(CollectionReservationReleaseOutcome.SkippedActiveLease,
                await store.ReleaseDispatchReservationAsync(wakeId, envelopeId, token, now.AddSeconds(1)),
                "A task lease also protects the reservation from a stale NoWork release.");
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var task = await db.Tasks.SingleAsync(x => x.TaskId == receipt.TaskId);
                task.LeaseToken = null;
                task.LeaseExpiresAt = null;
                await db.SaveChangesAsync();
            }
            Assert.AreEqual(CollectionReservationReleaseOutcome.Released,
                await store.ReleaseDispatchReservationAsync(wakeId, envelopeId, token, now.AddSeconds(1)));
            await using var verifyRelease = new CollectionPlatformDbContext(dbOptions);
            var row = await verifyRelease.DispatchOutbox.SingleAsync(x => x.OutboxId == pending.OutboxId);
            Assert.IsNull(row.ReservationToken);
            Assert.IsNull(row.ReservedUntilUnixMilliseconds);
            Assert.IsNull(row.EnvelopeId);
            Assert.IsNull(row.WakeId);
            Assert.IsNull(row.QueueMessageId);
            Assert.AreEqual(CollectionTaskStatus.Ready,
                (await verifyRelease.Tasks.SingleAsync(x => x.TaskId == receipt.TaskId)).Status,
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
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE collection_task_outbox DROP COLUMN WakeId; " +
                    "DROP TABLE collection_dispatcher_fairness_state; " +
                    "DELETE FROM collection_schema_history WHERE version = 19; " +
                    "INSERT OR IGNORE INTO collection_schema_history (version, applied_at) VALUES (18, 'v18-fixture');");
                Assert.AreEqual(0, await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM pragma_table_info('collection_task_outbox') WHERE name = 'WakeId'")
                    .SingleAsync());
                Assert.AreEqual(0, await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'collection_dispatcher_fairness_state'")
                    .SingleAsync());
                Assert.AreEqual(0, await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM collection_schema_history WHERE version = 19").SingleAsync());
                Assert.AreEqual(18, await db.Database.SqlQueryRaw<int>(
                    "SELECT COALESCE(MAX(version), 0) AS Value FROM collection_schema_history").SingleAsync());
            }

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
            var legacyWakeAcquire = await migrated.AcquireNextExecutionAsync(
                new(wakeId, envelopeId, token), "legacy-wake", now.AddSeconds(2), TimeSpan.FromSeconds(45));
            Assert.AreEqual(CollectionExecutionAcquireStatus.NoWork, legacyWakeAcquire.Status,
                "A legacy reservation without WakeId may only expire/re-dispatch; a new wake cannot claim it.");
            Assert.IsNull(preserved.DispatchedAt,
                "Rejecting an unverifiable legacy wake must not consume or clear its reservation.");
            Assert.AreEqual(1, await verify.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM collection_dispatcher_fairness_state WHERE StateId = 1")
                .SingleAsync(), "Schema migration initializes exactly one dispatcher fairness record.");
            Assert.AreEqual(19, await verify.Database.SqlQueryRaw<int>(
                "SELECT version AS Value FROM collection_schema_history ORDER BY version DESC LIMIT 1")
                .SingleAsync(), "The genuine v18 fixture must migrate and record schema version 19.");
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
            var acquired = await store.AcquireNextExecutionAsync(wake, "race-odds-message", requestedAt.AddSeconds(1),
                TimeSpan.FromSeconds(45), aggregationDelayMilliseconds: 60_000);
            Assert.AreEqual(CollectionExecutionAcquireStatus.Acquired, acquired.Status,
                "Acquire revalidation retains the race-odds aggregation-delay bypass.");
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
    public async Task AcquireEndpointRechecksConfiguredAggregationDelayAndRaceOddsBypass()
    {
        var root = Path.Combine(Path.GetTempPath(), "collection-acquire-aggregation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventDatabase = Path.Combine(root, "events.db");
        var (app, http) = await TestApplicationFactory.CreateAsync($"Data Source={eventDatabase}",
            aggregationDelayMilliseconds: 60_000);
        try
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var store = app.Services.GetRequiredService<CollectionPlatformStore>();
            var old = DateTimeOffset.UtcNow.AddMinutes(-2);
            var now = DateTimeOffset.UtcNow;
            var normalDefinition = new CollectionDefinitionId("race-detail");
            var oddsDefinition = new CollectionDefinitionId("race-odds");
            await store.RegisterDefinitionAsync(normalDefinition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            await store.RegisterDefinitionAsync(oddsDefinition, "Race odds", CollectionResourceType.RaceOdds,
                1, "initial", false);
            var normal = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "AGG-DELAY"),
                normalDefinition, 1, CollectionReason.Initial, old);
            var odds = await store.RequestAsync(new(CollectionResourceType.RaceOdds, "JRA", "ODDS-DELAY"),
                oddsDefinition, 1, CollectionReason.Initial, now);
            var pending = await store.GetPendingDispatchesAsync(now, 10, aggregationDelayMilliseconds: 60_000);
            var normalOutbox = pending.Single(x => x.Notification.TaskId == normal.TaskId);
            var oddsOutbox = pending.Single(x => x.Notification.TaskId == odds.TaskId);
            var normalWake = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "normal-delay-token");
            var oddsWake = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "odds-delay-token");
            Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([normalOutbox.OutboxId],
                normalWake.ReservationToken, normalWake.DispatchEnvelopeId, normalWake.WakeId, now,
                TimeSpan.FromMinutes(1), 2, 60_000));
            Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([oddsOutbox.OutboxId],
                oddsWake.ReservationToken, oddsWake.DispatchEnvelopeId, oddsWake.WakeId, now,
                TimeSpan.FromMinutes(1), 2, 60_000));

            var databasePath = Path.Combine(Path.GetFullPath(eventDatabase) + ".collection", "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var row = await db.DispatchOutbox.SingleAsync(x => x.OutboxId == normalOutbox.OutboxId);
                row.CreatedAt = now;
                await db.SaveChangesAsync();
            }

            var worker = new CollectionPlatformWorkerClient(http,
                new CollectionDefinitionHandlerRegistry(Array.Empty<ICollectionDefinitionHandler>()));
            var delayed = await worker.AcquireNextAsync(normalWake, "normal-delay-message", CancellationToken.None);
            var bypassed = await worker.AcquireNextAsync(oddsWake, "odds-delay-message", CancellationToken.None);

            Assert.AreEqual(CollectionExecutionAcquireStatus.NoWork, delayed.Status,
                "The endpoint must pass its configured delay to reservation revalidation.");
            Assert.AreEqual(CollectionExecutionAcquireStatus.Acquired, bypassed.Status,
                "The same endpoint configuration must not delay race-odds acquisition.");
            await using var verify = new CollectionPlatformDbContext(dbOptions);
            var preservedDelayed = await verify.DispatchOutbox.SingleAsync(x => x.OutboxId == normalOutbox.OutboxId);
            Assert.IsNull(preservedDelayed.DispatchedAt);
            Assert.IsNull(preservedDelayed.ReservationToken,
                "A safe typed NoWork result releases only its exact delay-ineligible reservation.");
            Assert.IsNull(preservedDelayed.WakeId);
            Assert.IsNull(preservedDelayed.EnvelopeId);
            Assert.AreEqual(CollectionTaskStatus.Ready,
                (await verify.Tasks.SingleAsync(x => x.TaskId == normal.TaskId)).Status,
                "Reservation release must preserve the task for later eligible dispatch.");
        }
        finally
        {
            http.Dispose();
            await app.DisposeAsync();
            Directory.Delete(root, recursive: true);
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
                    Assert.AreEqual(index + 1, (int)await db.Database.SqlQueryRaw<long>(
                        "SELECT ReservationSequence AS Value FROM collection_dispatcher_fairness_state WHERE StateId = 1")
                        .SingleAsync(), "The committed reservation advances fairness before queue send/acquisition.");
                    var acquired = await store.AcquireNextExecutionAsync(wake, $"fair-{index}", now,
                        TimeSpan.FromSeconds(45));
                    Assert.AreEqual(CollectionExecutionAcquireStatus.Acquired, acquired.Status);
                    var stateAfterRestart = await new CollectionPlatformStore(options).GetLaneDispatchStateAsync();
                    Assert.AreEqual(index + 1, (int)await db.Database.SqlQueryRaw<long>(
                        "SELECT ReservationSequence AS Value FROM collection_dispatcher_fairness_state WHERE StateId = 1")
                        .SingleAsync(), "Acquiring a wake must not advance fairness a second time.");
                    Assert.AreEqual(outbox.OutboxId, stateAfterRestart.ScanOutboxId,
                        "The keyset cursor must be written in the same transaction as reservation.");
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

    [TestMethod]
    public async Task LaneFairnessAdvancesAtReservationForMultipleUnacquiredWakes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-fairness-reserved-wakes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var store = new CollectionPlatformStore(options);
            var now = DateTimeOffset.UtcNow.AddMinutes(-10);
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var lanes = new[] { CollectionLane.Realtime, CollectionLane.Normal, CollectionLane.Background };
            for (var index = 0; index < 10; index++)
                foreach (var lane in lanes)
                    await store.RequestAsync(new(CollectionResourceType.Race, "JRA", $"MULTI-{lane}-{index}"),
                        definition, 1, CollectionReason.Initial, now, lane, 10);

            var queue = new WakeCaptureQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 10,
                    EnvelopeMaxTasks = 1,
                    MaxInFlightEnvelopes = 10,
                    OutboxReservationSeconds = 60,
                    AggregationDelayMilliseconds = 0
                }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            Assert.AreEqual(10, queue.Wakes.Count, "All ten available slots should become unacquired reservations.");
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            var granted = new List<CollectionLane>();
            foreach (var wake in queue.Wakes)
            {
                var outbox = await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId);
                await using var db = new CollectionPlatformDbContext(dbOptions);
                granted.Add((await db.Tasks.SingleAsync(x => x.TaskId == outbox.TaskId)).Lane);
            }
            CollectionLane[] expected = [CollectionLane.Realtime, CollectionLane.Realtime,
                CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Normal,
                CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime,
                CollectionLane.Realtime, CollectionLane.Background];
            CollectionAssert.AreEqual(expected, granted.ToArray(),
                "Realtime may burst four reservations, then Normal and Background alternate before acquisition.");
            var state = await new CollectionPlatformStore(options).GetLaneDispatchStateAsync();
            Assert.AreEqual(CollectionLane.Background, state.LastNonRealtimeLane);
            await using var stateDb = new CollectionPlatformDbContext(dbOptions);
            Assert.AreEqual(10L, (await stateDb.DispatcherFairnessStates.SingleAsync(x => x.StateId == 1))
                .ReservationSequence);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DispatcherReservationIsAtomicAcrossInstancesAndLoserContinuesToNextCandidate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-concurrent-dispatchers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var storeA = new CollectionPlatformStore(options);
            var storeB = new CollectionPlatformStore(options);
            var now = DateTimeOffset.UtcNow.AddMinutes(-5);
            var definition = new CollectionDefinitionId("race-detail");
            await storeA.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var first = await storeA.RequestAsync(new(CollectionResourceType.Race, "JRA", "CONCURRENT-1"),
                definition, 1, CollectionReason.Initial, now, CollectionLane.Background, 100);
            var second = await storeA.RequestAsync(new(CollectionResourceType.Race, "JRA", "CONCURRENT-2"),
                definition, 1, CollectionReason.Initial, now, CollectionLane.Background, 10);
            var queueA = new WakeCaptureQueue();
            var queueB = new WakeCaptureQueue();
            var optionsForDispatcher = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 2,
                OutboxReservationSeconds = 60,
                AggregationDelayMilliseconds = 0
            });
            var dispatcherA = new CollectionPlatformOutboxDispatcher(storeA, queueA, optionsForDispatcher,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var dispatcherB = new CollectionPlatformOutboxDispatcher(storeB, queueB, optionsForDispatcher,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var rendezvous = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var arrivals = 0;
            async Task Barrier(IReadOnlyList<PendingCollectionDispatch> _, CancellationToken token)
            {
                if (Interlocked.Increment(ref arrivals) == 2) rendezvous.TrySetResult();
                await rendezvous.Task.WaitAsync(token);
            }
            dispatcherA.BeforeReservationAsync = Barrier;
            dispatcherB.BeforeReservationAsync = Barrier;

            await Task.WhenAll(dispatcherA.DispatchOnceAsync(CancellationToken.None),
                dispatcherB.DispatchOnceAsync(CancellationToken.None));

            var wakes = queueA.Wakes.Concat(queueB.Wakes).ToArray();
            Assert.AreEqual(2, wakes.Length,
                "The contested first candidate gets one wake and the losing dispatcher continues with the next candidate.");
            Assert.AreEqual(2, wakes.Select(x => x.DispatchEnvelopeId).Distinct().Count());
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            var reservedTasks = new List<Guid>();
            foreach (var wake in wakes)
                reservedTasks.Add((await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId)).TaskId);
            CollectionAssert.AreEquivalent(new[] { first.TaskId!.Value, second.TaskId!.Value }, reservedTasks);
            await using var verify = new CollectionPlatformDbContext(dbOptions);
            Assert.AreEqual(2, await verify.DispatchOutbox.Where(x => x.ReservationToken != null)
                .Select(x => x.EnvelopeId).Distinct().CountAsync(), "Two dispatchers cannot exceed the two-slot limit.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ConcurrentRealtimeBurstGrantAllowsOnlyOneFourthRealtimeReservation()
    {
        var outcome = await DispatchConcurrentLaneConflictAsync("collection-concurrent-rt-grant",
            consecutiveRealtime: 3, lastNonRealtimeLane: CollectionLane.Background,
            realtimeCount: 2, normalCount: 1, backgroundCount: 1);

        CollectionAssert.AreEquivalent(new[] { CollectionLane.Realtime, CollectionLane.Normal }, outcome.Lanes.ToArray(),
            "With three prior realtime grants, one contender may grant the fourth RT slot; the other must reload and take Normal.");
        Assert.AreEqual(2, outcome.WakeCount);
        Assert.AreEqual(2, outcome.ReservedEnvelopeCount, "The concurrent pair must remain within MaxInFlight=2.");
    }

    [TestMethod]
    public async Task ConcurrentNonRealtimeConflictReloadsAndAlternatesNormalBackground()
    {
        var outcome = await DispatchConcurrentLaneConflictAsync("collection-concurrent-nonrt-turn",
            consecutiveRealtime: 4, lastNonRealtimeLane: CollectionLane.Normal,
            realtimeCount: 0, normalCount: 2, backgroundCount: 2);

        CollectionAssert.AreEquivalent(new[] { CollectionLane.Background, CollectionLane.Normal }, outcome.Lanes.ToArray(),
            "Two stale Background selections must serialize into Background then Normal.");
        Assert.AreEqual(2, outcome.WakeCount);
        Assert.AreEqual(2, outcome.ReservedEnvelopeCount, "Alternating lane grants must respect MaxInFlight=2.");
    }

    private static async Task<(IReadOnlyList<CollectionLane> Lanes, int WakeCount, int ReservedEnvelopeCount)>
        DispatchConcurrentLaneConflictAsync(string directoryName, int consecutiveRealtime,
            CollectionLane lastNonRealtimeLane, int realtimeCount, int normalCount, int backgroundCount)
    {
        var directory = Path.Combine(Path.GetTempPath(), directoryName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var storeA = new CollectionPlatformStore(options);
            var storeB = new CollectionPlatformStore(options);
            var requestedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var definition = new CollectionDefinitionId("race-detail");
            await storeA.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var index = 0;
            foreach (var (lane, count) in new[]
                     {
                         (CollectionLane.Realtime, realtimeCount),
                         (CollectionLane.Normal, normalCount),
                         (CollectionLane.Background, backgroundCount)
                     })
                for (var item = 0; item < count; item++)
                    await storeA.RequestAsync(new(CollectionResourceType.Race, "JRA", $"LANE-CONFLICT-{index++}"),
                        definition, 1, CollectionReason.Initial, requestedAt, lane, 10);

            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var state = await db.DispatcherFairnessStates.SingleAsync(x => x.StateId == 1);
                state.ConsecutiveRealtime = consecutiveRealtime;
                state.LastNonRealtimeLane = lastNonRealtimeLane.ToString();
                state.ScanAvailableAt = null;
                state.ScanOutboxId = null;
                await db.SaveChangesAsync();
            }

            var queueA = new WakeCaptureQueue();
            var queueB = new WakeCaptureQueue();
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 2,
                OutboxReservationSeconds = 60,
                AggregationDelayMilliseconds = 0
            });
            var dispatcherA = new CollectionPlatformOutboxDispatcher(storeA, queueA, queueOptions,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var dispatcherB = new CollectionPlatformOutboxDispatcher(storeB, queueB, queueOptions,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var rendezvous = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var arrivals = 0;
            async Task Barrier(IReadOnlyList<PendingCollectionDispatch> _, CancellationToken token)
            {
                if (Interlocked.Increment(ref arrivals) == 2) rendezvous.TrySetResult();
                await rendezvous.Task.WaitAsync(token);
            }
            dispatcherA.BeforeReservationAsync = Barrier;
            dispatcherB.BeforeReservationAsync = Barrier;

            await Task.WhenAll(dispatcherA.DispatchOnceAsync(CancellationToken.None),
                dispatcherB.DispatchOnceAsync(CancellationToken.None));

            var wakes = queueA.Wakes.Concat(queueB.Wakes).ToArray();
            var lanes = new List<CollectionLane>();
            foreach (var wake in wakes)
            {
                var outbox = await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId);
                await using var db = new CollectionPlatformDbContext(dbOptions);
                lanes.Add((await db.Tasks.SingleAsync(x => x.TaskId == outbox.TaskId)).Lane);
            }
            await using var verify = new CollectionPlatformDbContext(dbOptions);
            var reservedEnvelopeCount = await verify.DispatchOutbox.Where(x => x.ReservationToken != null)
                .Select(x => x.EnvelopeId).Distinct().CountAsync();
            return (lanes, wakes.Length, reservedEnvelopeCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FailedQueueSendKeepsReservationFairnessGrantAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-fairness-send-failure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var store = new CollectionPlatformStore(options);
            var now = DateTimeOffset.UtcNow.AddMinutes(-5);
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            for (var index = 0; index < 4; index++)
                await store.RequestAsync(new(CollectionResourceType.Race, "JRA", $"SEND-FAIL-{index}"),
                    definition, 1, CollectionReason.Initial, now, CollectionLane.Realtime, 10);
            var failedQueue = new FailingWakeQueue();
            var settings = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 1,
                OutboxReservationSeconds = 60,
                AggregationDelayMilliseconds = 0
            });
            await new CollectionPlatformOutboxDispatcher(store, failedQueue, settings,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance).DispatchOnceAsync(CancellationToken.None);
            var afterFailure = await new CollectionPlatformStore(options).GetLaneDispatchStateAsync();
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var persisted = await db.DispatcherFairnessStates.SingleAsync(x => x.StateId == 1);
                Assert.AreEqual(1L, persisted.ReservationSequence,
                    "A committed reservation advances persistent fairness even when SQS send fails.");
                Assert.AreEqual(CollectionLane.Realtime.ToString(), persisted.LastGrantedLane);
            }

            var restartedQueue = new WakeCaptureQueue();
            await new CollectionPlatformOutboxDispatcher(new CollectionPlatformStore(options), restartedQueue, settings,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance).DispatchOnceAsync(CancellationToken.None);
            var afterRestart = await new CollectionPlatformStore(options).GetLaneDispatchStateAsync();
            Assert.AreEqual(1, afterRestart.ConsecutiveRealtime,
                "A restart reloads the one committed realtime grant from the failed send.");
            await using var verify = new CollectionPlatformDbContext(dbOptions);
            Assert.AreEqual(1L, (await verify.DispatcherFairnessStates.SingleAsync(x => x.StateId == 1)).ReservationSequence,
                "Restart reads the committed failed-send grant, and the occupied slot prevents double reservation.");
            Assert.AreEqual(1, await verify.DispatchOutbox.CountAsync(x => x.ReservationToken != null),
                "The failed send retains its reservation until acquire/expiry.");
            Assert.AreEqual(0, restartedQueue.Wakes.Count);
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

    private sealed class FailingWakeQueue : ICollectionPlatformTaskQueue
    {
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new AssertFailedException("Only wake-only messages are expected.");

        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken) => throw new InvalidOperationException("simulated queue send failure");
    }
}
