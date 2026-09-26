using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionPlatformStoreTests
{
    [TestMethod]
    public async Task RaceDetail_ProvisionalCardIsSavedButDueUntilNumbersArrive()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260926:Nakayama:5");
        var now = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 5, "horse-key entries", false);
        var first = await store.RequestAsync(resource, definition, 5, CollectionReason.Initial, now,
            effectiveDate: new DateOnly(2026, 9, 26));
        var a = new RaceEntry(null, "木曜馬A", null, null, null, OwnerName: "馬主A",
            HorseSourceIdentity: "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026000101/00");
        var b = new RaceEntry(2, "木曜馬B", 1, null, null, OwnerName: "馬主B",
            HorseSourceIdentity: "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026000102/00");
        var cards = new FakeJraRaceCardCollectionWorkflow { OutcomeEntries = [a, b] };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(), _ => cards, _ => results,
            timeProvider: new FixedTimeProvider(now));

        var firstLease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(firstLease);
        var waiting = await handler.CollectAsync(firstLease, CancellationToken.None);
        Assert.AreEqual("RaceHorseNumbersUnconfirmed", waiting.ErrorCode);
        Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId.Value, firstLease.LeaseToken,
            now.AddSeconds(1), waiting));
        var provisional = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!
            .Single(x => x.Artifact == RaceArtifactKind.Card);
        Assert.AreEqual(RaceArtifactStatus.AwaitingPublication, provisional.Status);
        Assert.IsNotNull(provisional.LastPersistedAt);
        Assert.AreEqual(5, provisional.AppliedRevision);
        Assert.IsEmpty(results.Requests);

        cards.OutcomeEntries = [a with { HorseNumber = 1, FrameNumber = 1 }, b];
        var secondLease = await store.AcquireAsync(first.TaskId.Value, 2, now.AddMinutes(16), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(secondLease);
        Assert.AreEqual("AwaitingPublication", secondLease.Attributes["cardArtifactStatus"]);
        var afterNumbers = await handler.CollectAsync(secondLease, CancellationToken.None);
        Assert.HasCount(2, cards.RefreshRequests);
        Assert.IsTrue(afterNumbers.StageOutcomes!.Any(x => x.Stage == "PersistCard" && x.Persisted));
        Assert.IsFalse(afterNumbers.StageOutcomes!.Any(x => x.Stage == "AwaitHorseNumbers"));
        Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId.Value, secondLease.LeaseToken,
            now.AddMinutes(17), afterNumbers));
        var confirmed = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!
            .Single(x => x.Artifact == RaceArtifactKind.Card);
        Assert.AreEqual(RaceArtifactStatus.Current, confirmed.Status);
        Assert.IsEmpty(results.Requests);
    }

    [TestMethod]
    public async Task RaceDetail_RevisionFour_UnconfirmedNumbersWaitWithoutSaving_ThenConfirmedCardUpgrades()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260926:Nakayama:5");
        var now = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 3, "old", false);
        var initial = await store.RequestAsync(resource, definition, 3, CollectionReason.Initial, now, effectiveDate: new DateOnly(2026, 9, 26));
        var oldLease = await store.AcquireAsync(initial.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(initial.TaskId!.Value, oldLease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, StageOutcomes: [new("PersistCard", RaceArtifactKind.Card, CollectionAttemptResult.Succeeded, Persisted: true)])));
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 4, "confirmed numbers", false);
        var refresh = await store.RequestAsync(resource, definition, 4, CollectionReason.DefinitionChanged, now.AddSeconds(2));
        var lease = await store.AcquireAsync(refresh.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        var cards = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraHorseNumbersUnconfirmedException("https://www.jra.go.jp/JRADB/accessD.html", new(new(2026, 9, 26), RaceCourse.Nakayama, 5))
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(), _ => cards, _ => results, timeProvider: new FixedTimeProvider(now));
        var waiting = await handler.CollectAsync(lease, CancellationToken.None);
        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, waiting.Result);
        Assert.AreEqual("RaceHorseNumbersUnconfirmed", waiting.ErrorCode);
        Assert.IsEmpty(results.Requests);
        Assert.IsTrue(waiting.StageOutcomes!.All(x => !x.Persisted));
        Assert.IsTrue(await store.CompleteAttemptAsync(refresh.TaskId!.Value, lease.LeaseToken, now.AddSeconds(3), waiting));
        var card = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!.Single(x => x.Artifact == RaceArtifactKind.Card);
        Assert.AreEqual(3, card.AppliedRevision);
        cards.ThrowOnCollect = null;
        var confirmedLease = await store.AcquireAsync(refresh.TaskId!.Value, 2, now.AddMinutes(16), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(confirmedLease);
        var confirmed = await handler.CollectAsync(confirmedLease, CancellationToken.None);
        Assert.IsTrue(confirmed.StageOutcomes!.Any(x => x.Artifact == RaceArtifactKind.Card && x.Persisted));
        Assert.IsTrue(await store.CompleteAttemptAsync(refresh.TaskId!.Value, confirmedLease.LeaseToken, now.AddMinutes(17), confirmed));
        Assert.AreEqual(4, (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!.Single(x => x.Artifact == RaceArtifactKind.Card).AppliedRevision);
    }

    [TestMethod]
    public async Task ObsoleteSubjectProjectionCleanup_CancelsRaceDerivedRetryAndKeepsHistory()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string>
            {
                ["name"] = "obsolete",
                ["requestedByRaceId"] = "race-authoritative",
            });
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotYetAvailable, "SubjectProjectionNotReady", "missing",
                RetryAt: now.AddHours(1))));

        var preview = await store.GetObsoleteSubjectProfileTasksAsync();
        Assert.HasCount(1, preview);
        Assert.AreEqual(receipt.TaskId!.Value, preview[0].TaskId);

        var result = await store.RetireObsoleteSubjectProfileTasksAsync([receipt.TaskId!.Value], now.AddSeconds(2));

        Assert.AreEqual(1, result.CancelledCount);
        Assert.AreEqual(0, result.RunningCancellationRequests);
        Assert.IsEmpty(await store.GetObsoleteSubjectProfileTasksAsync());
        var task = (await store.GetTasksAsync(limit: 10)).Single(item => item.TaskId == receipt.TaskId!.Value);
        Assert.AreEqual(CollectionTaskStatus.Cancelled, task.Status);
        Assert.HasCount(1, await store.GetAttemptsAsync(receipt.TaskId!.Value));
    }

    [TestMethod]
    public async Task ObsoleteSubjectProfileTasks_IncludesTerminalResourceMissingAndPreservesEvidence()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(
            new(ResourceType.Horse, "JRA", "legacy-horse"), HorseProfile, 7,
            CollectionReason.Discovery, now, CollectionLane.Realtime, (int)CollectionPriority.High,
            explicitUrl: new Uri("https://www.jra.go.jp/JRADB/accessU.html?CNAME=legacy"),
            effectiveDate: new(2026, 9, 18),
            attributes: new Dictionary<string, string>
            {
                ["name"] = "Legacy Horse",
                ["sourceIdentity"] = "jra:legacy",
                ["sourceUrl"] = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=legacy",
                ["requestedByRaceId"] = "race-1",
            });
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "target missing",
                FailureImpact: CollectionFailureImpact.Isolated)));

        var candidates = await store.GetObsoleteSubjectProfileTasksAsync();

        var candidate = candidates.Single();
        Assert.AreEqual(receipt.TaskId!.Value, candidate.TaskId);
        Assert.AreEqual(CollectionTaskStatus.Failed, candidate.Status);
        Assert.AreEqual("Legacy Horse", candidate.Name);
        Assert.AreEqual("jra:legacy", candidate.SourceIdentity);
        Assert.AreEqual("https://www.jra.go.jp/JRADB/accessU.html?CNAME=legacy", candidate.SourceUrl!.AbsoluteUri);
        Assert.AreEqual("race-1", candidate.RequestedByRaceId);
        Assert.AreEqual(7, candidate.RequestedRevision);
        Assert.AreEqual(CollectionLane.Realtime, candidate.Lane);
        Assert.AreEqual((int)CollectionPriority.High, candidate.Priority);
        Assert.AreEqual(new(2026, 9, 18), candidate.EffectiveDate);
        Assert.AreEqual(CollectionAttemptResult.PermanentFailure, candidate.LatestResult);
    }

    [TestMethod]
    public async Task ObsoleteSubjectProfileTasks_ExcludesUnrelatedErrorsAndNonRaceDerivedWork()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);

        await CreateFailedTask("unrelated", "SubjectProjectionNotReady", null);
        await CreateFailedTask("non-race", "SubjectResourceMissing", "   ");
        await CreateFailedTask("different-error", "Timeout", "race-1");

        Assert.IsEmpty(await store.GetObsoleteSubjectProfileTasksAsync());

        async Task CreateFailedTask(string id, string errorCode, string? raceId)
        {
            var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", id), HorseProfile, 7,
                CollectionReason.Discovery, now,
                attributes: raceId is null
                    ? new Dictionary<string, string> { ["name"] = id }
                    : new Dictionary<string, string> { ["name"] = id, ["requestedByRaceId"] = raceId });
            var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
            Assert.IsNotNull(lease);
            Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.PermanentFailure, errorCode, "failure",
                    FailureImpact: CollectionFailureImpact.Isolated)));
        }
    }

    [TestMethod]
    public async Task ObsoleteSubjectProfileTasks_RunningRetryRetainsFailureEvidenceAndCancelsOnCompletion()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", "running-legacy"), HorseProfile, 7,
            CollectionReason.Discovery, now, attributes: new Dictionary<string, string>
            {
                ["name"] = "Running Legacy",
                ["requestedByRaceId"] = "race-running",
            });
        var firstLease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(firstLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, firstLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotYetAvailable, "SubjectProjectionNotReady", "missing",
                RetryAt: now.AddSeconds(2))));
        var runningLease = await store.AcquireAsync(receipt.TaskId!.Value, 2, now.AddSeconds(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(runningLease);

        var candidate = (await store.GetObsoleteSubjectProfileTasksAsync()).Single();
        Assert.AreEqual(CollectionTaskStatus.Running, candidate.Status);
        Assert.AreEqual("SubjectProjectionNotReady", candidate.ErrorCode);
        var retirement = await store.RetireObsoleteSubjectProfileTasksAsync([receipt.TaskId!.Value], now.AddSeconds(3));
        Assert.AreEqual(1, retirement.RunningCancellationRequests);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, runningLease.LeaseToken, now.AddSeconds(4),
            new(CollectionAttemptResult.Succeeded)));
        Assert.AreEqual(CollectionTaskStatus.Cancelled,
            (await store.GetTasksAsync(limit: 1000)).Single(item => item.TaskId == receipt.TaskId!.Value).Status);
    }

    [TestMethod]
    public async Task RequestManyAsync_CommitsOneDatabaseTransaction()
    {
        var path = Path.Combine(_directory, "transaction-counter.db");
        var counter = new TransactionCounterInterceptor();
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .AddInterceptors(counter).Options;
        var store = new CollectionPlatformStore(options);
        await store.RegisterDefinitionAsync(HorseProfile, "Horse profile", ResourceType.Horse, 7,
            "Initial profile extractor", false);
        counter.Commits = 0;

        await store.RequestManyAsync("race-subjects:race-1",
        [
            new("Horse:H001", new(ResourceType.Horse, "JRA", "H001"), HorseProfile, 7,
                CollectionReason.Discovery, CollectionLane.Realtime, 70, null, new(2026, 9, 12), null),
            new("Horse:H002", new(ResourceType.Horse, "JRA", "H002"), HorseProfile, 7,
                CollectionReason.Discovery, CollectionLane.Realtime, 70, null, new(2026, 9, 12), null),
        ], DateTimeOffset.UtcNow);

        Assert.AreEqual(1, counter.Commits);
    }

    [TestMethod]
    public async Task RequestManyAsync_AcceptsAndPersistsReferenceRaceMetadata()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var attributes = new Dictionary<string, string>
        {
            ["referenceRaceDate"] = "2026-09-19",
            ["referenceRaceCourse"] = "Nakayama",
            ["referenceRaceNumber"] = "2",
        };
        var item = new CollectionRequestBatchItem("Horse:H001", new(ResourceType.Horse, "JRA", "H001"),
            HorseProfile, 7, CollectionReason.Discovery, CollectionLane.Realtime, 70, null,
            new DateOnly(2026, 9, 19), attributes);

        var outcome = (await store.RequestManyAsync("race-subjects:race-1", [item], now)).Single();

        Assert.AreEqual("Created", outcome.Status);
        var lease = await store.AcquireAsync(outcome.Receipt!.TaskId!.Value, 1, now.AddSeconds(1), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.AreEqual("2026-09-19", lease.Attributes["referenceRaceDate"]);
        Assert.AreEqual("Nakayama", lease.Attributes["referenceRaceCourse"]);
        Assert.AreEqual("2", lease.Attributes["referenceRaceNumber"]);
    }

    [TestMethod]
    public async Task RequestManyAsync_ConcurrentStoresBindSamePayloadAndRejectDifferentPayload()
    {
        var firstStore = await CreateStoreAsync();
        var secondStore = CreateStore();
        var item = new CollectionRequestBatchItem("Horse:H001", new(ResourceType.Horse, "JRA", "H001"),
            HorseProfile, 7, CollectionReason.Discovery, CollectionLane.Realtime, 70, null,
            new(2026, 9, 12), null);
        var now = DateTimeOffset.UtcNow;

        var same = await Task.WhenAll(
            firstStore.RequestManyAsync("race-subjects:same", [item], now),
            secondStore.RequestManyAsync("race-subjects:same", [item], now));

        Assert.AreEqual(same[0][0].Receipt!.RequestId, same[1][0].Receipt!.RequestId);
        CollectionAssert.AreEquivalent(new[] { "Created", "Reused" },
            same.Select(x => x[0].Status).ToArray());

        var different = item with { Priority = 99 };
        var conflict = await Task.WhenAll(
            firstStore.RequestManyAsync("race-subjects:different", [item], now),
            secondStore.RequestManyAsync("race-subjects:different", [different], now));
        CollectionAssert.AreEquivalent(new[] { "Reused", "Rejected" },
            conflict.Select(x => x[0].Status).ToArray());
        Assert.IsTrue(conflict.Select(x => x[0]).Any(x => x.ErrorCode == "IdempotencyMismatch"));
    }

    [TestMethod]
    public async Task RequestManyAsync_ReturnsPerItemOutcomes_AndReusesReplay()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        CollectionRequestBatchItem[] items =
        [
            new("Horse:H001", new(ResourceType.Horse, "JRA", "H001"), HorseProfile, 7,
                CollectionReason.Discovery, CollectionLane.Realtime, 70, null, new(2026, 9, 12), null),
            new("Horse:H002", new(ResourceType.Horse, "JRA", "H002"), HorseProfile, 7,
                CollectionReason.Discovery, CollectionLane.Realtime, 70, null, new(2026, 9, 12), null),
            new("Horse:H003", new(ResourceType.Horse, "JRA", "H003"), new("missing-definition"), 1,
                CollectionReason.Discovery, CollectionLane.Realtime, 70, null, new(2026, 9, 12), null),
        ];

        var first = await store.RequestManyAsync("race-subjects:race-1", items, now);
        var replay = await store.RequestManyAsync("race-subjects:race-1", items, now.AddMinutes(1));

        CollectionAssert.AreEqual(new[] { "Created", "Created", "Rejected" },
            first.Select(x => x.Status).ToArray());
        CollectionAssert.AreEqual(new[] { "Reused", "Reused", "Rejected" },
            replay.Select(x => x.Status).ToArray());
        CollectionAssert.AreEqual(first.Take(2).Select(x => x.Receipt!.RequestId).ToArray(),
            replay.Take(2).Select(x => x.Receipt!.RequestId).ToArray());
        Assert.AreEqual("InvalidRequest", first[2].ErrorCode);
        Assert.HasCount(2, await store.GetTasksAsync());

        var changed = items.ToArray();
        changed[0] = changed[0] with { Priority = 99 };
        var mismatch = await store.RequestManyAsync("race-subjects:race-1", changed, now.AddMinutes(2));
        Assert.AreEqual("Rejected", mismatch[0].Status);
        Assert.AreEqual("IdempotencyMismatch", mismatch[0].ErrorCode);
        Assert.HasCount(2, await store.GetTasksAsync());

        changed[0] = changed[0] with
        {
            Resource = new ResourceKey(ResourceType.Horse, "JRA", "DIFFERENT-HORSE"),
            Priority = items[0].Priority,
        };
        var rebound = await store.RequestManyAsync("race-subjects:race-1", changed, now.AddMinutes(3));
        Assert.AreEqual("Rejected", rebound[0].Status);
        Assert.AreEqual("IdempotencyMismatch", rebound[0].ErrorCode);
        Assert.HasCount(2, await store.GetTasksAsync());
    }

    [TestMethod]
    public async Task RequestManyAsync_DifferentHorsesReuseOneRaceTaskAndOutboxMessage()
    {
        var store = CreateStore();
        var concurrentStore = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 1,
            "Initial race extractor", false);
        var now = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        var race = new ResourceKey(ResourceType.Race, "JRA", "20260913:Nakayama:11");

        CollectionRequestBatchItem Item(string horseId) => new(
            race.Id, race, definition, 1, CollectionReason.Discovery, CollectionLane.Background, 30,
            new Uri("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604020520260913/2F"),
            new DateOnly(2026, 9, 13),
            new Dictionary<string, string> { ["requestedByHorseId"] = horseId });

        var concurrent = await Task.WhenAll(
            store.RequestManyAsync("horse-history:horse-a:p0:c0", [Item("horse-a")], now),
            concurrentStore.RequestManyAsync("horse-history:horse-b:p0:c0", [Item("horse-b")], now));
        var first = concurrent[0].Single();
        var second = concurrent[1].Single();

        CollectionAssert.AreEquivalent(new[] { "Created", "Reused" },
            new[] { first.Status, second.Status });
        Assert.AreEqual(first.Receipt!.TaskId, second.Receipt!.TaskId);
        Assert.AreEqual(first.Receipt.RequestId, second.Receipt.RequestId);
        Assert.HasCount(1, await store.GetTasksAsync());
        Assert.HasCount(1, await store.GetPendingDispatchesAsync(now.AddSeconds(2), 10));

        var created = first.Status == "Created" ? first : second;
        await CompleteAsync(store, created.Receipt!, now.AddSeconds(2));
        var third = (await store.RequestManyAsync("horse-history:horse-c:p0:c0", [Item("horse-c")],
            now.AddMinutes(2))).Single();

        Assert.AreEqual("Reused", third.Status);
        Assert.AreEqual(first.Receipt.TaskId, third.Receipt!.TaskId);
        Assert.HasCount(1, await store.GetTasksAsync());
        Assert.HasCount(1, await store.GetPendingDispatchesAsync(now.AddMinutes(2), 10));
    }

    [TestMethod]
    public async Task RequestManyAsync_RejectsDuplicateItemKeysBeforeCreatingTasks()
    {
        var store = await CreateStoreAsync();
        var item = new CollectionRequestBatchItem("Horse:H001", new(ResourceType.Horse, "JRA", "H001"),
            HorseProfile, 7, CollectionReason.Discovery, CollectionLane.Realtime, 70, null,
            new(2026, 9, 12), null);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.RequestManyAsync("race-subjects:race-1", [item, item], DateTimeOffset.UtcNow));
        Assert.IsEmpty(await store.GetTasksAsync());
    }

    [TestMethod]
    public async Task TaskMetadata_RemainsImmutableWhenLaterRequestUpdatesResourceAttributes()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery, now,
            attributes: new Dictionary<string, string> { ["name"] = "original", ["sourceIdentity"] = "source-1" });
        var reused = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now.AddMinutes(1),
            attributes: new Dictionary<string, string> { ["name"] = "updated", ["sourceIdentity"] = "source-2" });

        Assert.AreEqual(first.TaskId!.Value, reused.TaskId!.Value);
        var lease = await store.AcquireAsync(first.TaskId!.Value, 1, now.AddMinutes(1), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.AreEqual("original", lease.Attributes["name"]);
        Assert.AreEqual("source-1", lease.Attributes["sourceIdentity"]);
    }

    [TestMethod]
    [DataRow("{\"weekendPriorityUntil\":\"2026-10-04\"}")]
    [DataRow("{}")]
    public async Task WeekendDispatchCompatibility_UsesTaskSnapshotWhenResourceAttributesChange(
        string replacementAttributes)
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            CollectionLane.Realtime, attributes: new Dictionary<string, string>
            {
                ["weekendPriorityUntil"] = "2026-09-27",
            });
        await UpdatePersistedCompatibilityAttributesAsync(receipt.TaskId!.Value, replacementAttributes);

        var pending = (await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10)).Single();
        Assert.AreEqual("2026-09-27", pending.Attributes!["weekendPriorityUntil"]);
        var envelopeId = Guid.NewGuid();
        var reservation = Guid.NewGuid().ToString("N");
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending.OutboxId], reservation,
            envelopeId, now, TimeSpan.FromSeconds(45), 1));
        var wake = new CollectionWakeSignal(Guid.NewGuid(), envelopeId, reservation);
        var acquiredExecution = await store.AcquireNextExecutionAsync(wake, "message-1", now.AddSeconds(1),
            TimeSpan.FromSeconds(45));
        var rebuiltExecution = await store.AcquireNextExecutionAsync(wake, "message-1", now.AddSeconds(2),
            TimeSpan.FromSeconds(45));
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));

        Assert.AreEqual(CollectionDispatchGroupKind.WeekendSubjects,
            acquiredExecution.Envelope!.Compatibility.GroupKind);
        Assert.AreEqual("2026-09-27", acquiredExecution.Envelope.Compatibility.GroupKey);
        Assert.AreEqual("2026-09-27", rebuiltExecution.Envelope!.Compatibility.GroupKey);
        Assert.AreEqual("2026-09-27", lease!.Attributes["weekendPriorityUntil"]);
    }

    [TestMethod]
    public async Task WeekendDispatchCompatibility_NullTaskMetadataFallsBackToResourceAttributes()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            CollectionLane.Realtime, attributes: new Dictionary<string, string>
            {
                ["weekendPriorityUntil"] = "2026-09-27",
            });
        await UpdatePersistedCompatibilityAttributesAsync(receipt.TaskId!.Value,
            "{\"weekendPriorityUntil\":\"2026-10-04\"}", clearTaskMetadata: true);

        var pending = (await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10)).Single();
        Assert.AreEqual("2026-10-04", pending.Attributes!["weekendPriorityUntil"]);
        var envelopeId = Guid.NewGuid();
        var reservation = Guid.NewGuid().ToString("N");
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending.OutboxId], reservation,
            envelopeId, now, TimeSpan.FromSeconds(45), 1));
        var acquiredExecution = await store.AcquireNextExecutionAsync(
            new(Guid.NewGuid(), envelopeId, reservation), "message-legacy", now.AddSeconds(1),
            TimeSpan.FromSeconds(45));
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));

        Assert.AreEqual(CollectionDispatchGroupKind.WeekendSubjects,
            acquiredExecution.Envelope!.Compatibility.GroupKind);
        Assert.AreEqual("2026-10-04", acquiredExecution.Envelope.Compatibility.GroupKey);
        Assert.AreEqual("2026-10-04", lease!.Attributes["weekendPriorityUntil"]);
    }

    private async Task UpdatePersistedCompatibilityAttributesAsync(Guid taskId, string resourceAttributes,
        bool clearTaskMetadata = false)
    {
        await using var db = new CollectionPlatformDbContext(new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False").Options);
        var task = await db.Tasks.SingleAsync(x => x.TaskId == taskId);
        var resource = await db.Resources.SingleAsync(x => x.ResourcePk == task.ResourcePk);
        resource.AttributesJson = resourceAttributes;
        if (clearTaskMetadata) task.MetadataJson = null;
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task ManualRefresh_WithoutMetadata_InheritsCompletedTaskSnapshot()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var original = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string>
            {
                ["name"] = "村山 明（栗東）",
                ["discoveredFromId"] = "horse-source",
            });
        var originalLease = await store.AcquireAsync(original.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(original.TaskId!.Value, originalLease!.LeaseToken,
            now.AddSeconds(1), new(CollectionAttemptResult.Succeeded)));

        var refresh = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh,
            now.AddSeconds(2));
        var refreshLease = await store.AcquireAsync(refresh.TaskId!.Value, 1, now.AddSeconds(2),
            TimeSpan.FromMinutes(5));

        Assert.IsNotNull(refreshLease);
        Assert.AreEqual("村山 明（栗東）", refreshLease.Attributes["name"]);
        Assert.AreEqual("horse-source", refreshLease.Attributes["discoveredFromId"]);
    }

    [TestMethod]
    public async Task ScheduledRefresh_WithoutMetadata_InheritsResourceSnapshotAndKeepsTaskImmutable()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var url = new Uri("https://www.jra.go.jp/JRADB/accessU.html?CNAME=horse-source");
        var original = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            explicitUrl: url, attributes: new Dictionary<string, string>
            {
                ["name"] = "定期更新馬",
                ["sourceIdentity"] = url.AbsoluteUri,
                ["discoveredFromId"] = "race-source",
            });
        await CompleteAsync(store, original, now);

        var refresh = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ScheduledRefresh,
            now.AddMinutes(2));
        await UpdatePersistedCompatibilityAttributesAsync(refresh.TaskId!.Value,
            "{\"name\":\"後続resource名\",\"sourceIdentity\":\"changed\"}");

        var pending = (await store.GetPendingDispatchesAsync(now.AddMinutes(3), 10))
            .Single(x => x.Notification.TaskId == refresh.TaskId!.Value);
        var lease = await store.AcquireAsync(refresh.TaskId!.Value, 1, now.AddMinutes(3), TimeSpan.FromMinutes(5));

        Assert.AreEqual("定期更新馬", pending.Attributes!["name"]);
        Assert.AreEqual(url.AbsoluteUri, pending.Attributes["sourceIdentity"]);
        Assert.AreEqual("定期更新馬", lease!.Attributes["name"]);
        Assert.AreEqual(url.AbsoluteUri, lease.Attributes["sourceIdentity"]);
        Assert.AreEqual("race-source", lease.Attributes["discoveredFromId"]);
        Assert.AreEqual(url, lease.Locations!.Single().Url);
    }

    [TestMethod]
    public async Task ScheduledRefresh_ExplicitMetadataReplacesResourceSnapshot()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var original = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string>
            {
                ["name"] = "旧名",
                ["sourceIdentity"] = "old-source",
            });
        await CompleteAsync(store, original, now);

        var explicitRefresh = await store.RequestAsync(Horse, HorseProfile, 7,
            CollectionReason.ScheduledRefresh, now.AddMinutes(2),
            attributes: new Dictionary<string, string> { ["sourceIdentity"] = "new-source" });
        var explicitLease = await store.AcquireAsync(explicitRefresh.TaskId!.Value, 1, now.AddMinutes(2),
            TimeSpan.FromMinutes(5));
        Assert.IsNotNull(explicitLease);
        Assert.AreEqual("new-source", explicitLease.Attributes["sourceIdentity"]);
        Assert.IsFalse(explicitLease.Attributes.ContainsKey("name"));
        Assert.IsTrue(await store.CompleteAttemptAsync(explicitRefresh.TaskId!.Value, explicitLease.LeaseToken,
            now.AddMinutes(3), new(CollectionAttemptResult.Succeeded)));

        var inherited = await store.RequestAsync(Horse, HorseProfile, 7,
            CollectionReason.ScheduledRefresh, now.AddMinutes(4));
        var inheritedLease = await store.AcquireAsync(inherited.TaskId!.Value, 1, now.AddMinutes(4),
            TimeSpan.FromMinutes(5));
        Assert.IsNotNull(inheritedLease);
        Assert.AreEqual("new-source", inheritedLease.Attributes["sourceIdentity"]);
        Assert.IsFalse(inheritedLease.Attributes.ContainsKey("name"));
    }

    [TestMethod]
    public async Task ScheduledRefresh_ExplicitEmptyMetadataRemainsEmpty()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var original = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = "明示消去前" });
        await CompleteAsync(store, original, now);

        var explicitEmpty = await store.RequestAsync(Horse, HorseProfile, 7,
            CollectionReason.ScheduledRefresh, now.AddMinutes(2), attributes: new Dictionary<string, string>());
        var emptyLease = await store.AcquireAsync(explicitEmpty.TaskId!.Value, 1, now.AddMinutes(2),
            TimeSpan.FromMinutes(5));
        Assert.IsNotNull(emptyLease);
        Assert.IsEmpty(emptyLease.Attributes);
        Assert.IsTrue(await store.CompleteAttemptAsync(explicitEmpty.TaskId!.Value, emptyLease.LeaseToken,
            now.AddMinutes(3), new(CollectionAttemptResult.Succeeded)));

        var inheritedEmpty = await store.RequestAsync(Horse, HorseProfile, 7,
            CollectionReason.ScheduledRefresh, now.AddMinutes(4));
        var inheritedEmptyLease = await store.AcquireAsync(inheritedEmpty.TaskId!.Value, 1, now.AddMinutes(4),
            TimeSpan.FromMinutes(5));
        Assert.IsNotNull(inheritedEmptyLease);
        Assert.IsEmpty(inheritedEmptyLease.Attributes);
    }

    [TestMethod]
    public async Task Recovery_WithoutMetadata_SkipsLatestEmptySnapshot()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var original = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string>
            {
                ["name"] = "村山 明（栗東）",
                ["discoveredFromId"] = "horse-source",
            });
        var originalLease = await store.AcquireAsync(original.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(original.TaskId!.Value, originalLease!.LeaseToken,
            now.AddSeconds(1), new(CollectionAttemptResult.Succeeded)));
        var empty = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ScheduledRefresh,
            now.AddSeconds(2), attributes: new Dictionary<string, string>());
        var emptyLease = await store.AcquireAsync(empty.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(empty.TaskId!.Value, emptyLease!.LeaseToken,
            now.AddSeconds(3), new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified")));

        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery,
            now.AddSeconds(4));
        var recoveryLease = await store.AcquireAsync(recovery.TaskId!.Value, 1, now.AddSeconds(4),
            TimeSpan.FromMinutes(5));

        Assert.IsNotNull(recoveryLease);
        Assert.AreEqual("村山 明（栗東）", recoveryLease.Attributes["name"]);
        Assert.AreEqual("horse-source", recoveryLease.Attributes["discoveredFromId"]);
    }

    [TestMethod]
    public async Task Recovery_MetadataOverridesOnlySpecifiedKeys()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var original = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string>
            {
                ["name"] = "旧名",
                ["sourceIdentity"] = "source-1",
                ["discoveredFromId"] = "horse-source",
            });
        var originalLease = await store.AcquireAsync(original.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(original.TaskId!.Value, originalLease!.LeaseToken,
            now.AddSeconds(1), new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified")));

        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery,
            now.AddSeconds(2), attributes: new Dictionary<string, string> { ["name"] = "補正名" });
        var recoveryLease = await store.AcquireAsync(recovery.TaskId!.Value, 1, now.AddSeconds(2),
            TimeSpan.FromMinutes(5));

        Assert.IsNotNull(recoveryLease);
        Assert.AreEqual("補正名", recoveryLease.Attributes["name"]);
        Assert.AreEqual("source-1", recoveryLease.Attributes["sourceIdentity"]);
        Assert.AreEqual("horse-source", recoveryLease.Attributes["discoveredFromId"]);
    }

    [TestMethod]
    public async Task Recovery_DoesNotInheritAnotherResourcesSnapshot()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var prior = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = "other-resource" });
        await CompleteAsync(store, prior, now);
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", "different-horse"),
            HorseProfile, 7, CollectionReason.Recovery, now.AddMinutes(2));
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now.AddMinutes(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsFalse(lease.Attributes.ContainsKey("name"));
    }

    [TestMethod]
    public async Task Recovery_PriorSnapshotDoesNotMixNewResourceAttributes()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var prior = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = "snapshot-name" });
        await CompleteAsync(store, prior, now);
        var otherDefinition = new CollectionDefinitionId("other-profile");
        await store.RegisterDefinitionAsync(otherDefinition, "Other profile", ResourceType.Horse, 1, "initial", false);
        var other = await store.RequestAsync(Horse, otherDefinition, 1, CollectionReason.Discovery,
            now.AddMinutes(2), attributes: new Dictionary<string, string>
            {
                ["name"] = "resource-name",
                ["sourceIdentity"] = "other-definition",
            });
        await CompleteAsync(store, other, now.AddMinutes(2));
        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery, now.AddMinutes(4));
        var lease = await store.AcquireAsync(recovery.TaskId!.Value, 1, now.AddMinutes(4), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.AreEqual("snapshot-name", lease.Attributes["name"]);
        Assert.IsFalse(lease.Attributes.ContainsKey("sourceIdentity"));
    }

    [TestMethod]
    public async Task KeyedRecovery_RecreatesTaskWhenOriginallyReusedActiveTaskHasBecomeTerminal()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var active = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now);
        var keyed = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery,
            now.AddSeconds(1), batchId: "repair:one", payloadFingerprint: "target-one");
        Assert.AreEqual(active.TaskId!.Value, keyed.TaskId!.Value);
        var lease = await store.AcquireAsync(active.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(active.TaskId!.Value, lease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.Succeeded)));

        var replay = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery,
            now.AddSeconds(4), batchId: "repair:one", payloadFingerprint: "target-one");

        Assert.AreNotEqual(Guid.Empty, replay.TaskId!.Value);
        Assert.AreNotEqual(active.TaskId!.Value, replay.TaskId!.Value);
        Assert.IsTrue(replay.CreatedTask);
        Assert.AreEqual(keyed.RequestId, replay.RequestId);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync(limit: 10)).Single(x => x.TaskId == replay.TaskId!.Value).Status);
    }

    [TestMethod]
    public async Task TaskMetadata_RejectsSecretBearingKeysBeforePersistence()
    {
        var store = await CreateStoreAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.RequestAsync(Horse, HorseProfile, 7,
            CollectionReason.Initial, DateTimeOffset.UtcNow,
            attributes: new Dictionary<string, string> { ["authorizationToken"] = "secret" }));
        Assert.IsEmpty(await store.GetTasksAsync());
    }

    private string _directory = null!;
    private static readonly CollectionDefinitionId HorseProfile = new("horse-profile");
    private static readonly ResourceKey Horse = new(ResourceType.Horse, "jra", "H123");

    [TestMethod]
    [DataRow("Succeeded")]
    [DataRow("Failed")]
    [DataRow("Cancelled")]
    [DataRow("DeadLetter")]
    public async Task TerminalTask_MaterializesExactlyOneHigherRevisionRequest(string terminal)
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(HorseProfile, "Horse profile", ResourceType.Horse, 1,
            "Initial revision", false);
        var now = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var first = await store.RequestAsync(Horse, HorseProfile, 1, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        await store.RegisterDefinitionAsync(HorseProfile, "Horse profile", ResourceType.Horse, 2,
            "Revision materialization", true);
        var higher = await store.RequestAsync(Horse, HorseProfile, 2, CollectionReason.DefinitionChanged,
            now.AddSeconds(1), CollectionLane.Background, (int)CollectionPriority.Background,
            attributes: new Dictionary<string, string> { ["name"] = "new-revision" });
        Assert.AreEqual(first.TaskId!.Value, higher.TaskId!.Value);

        switch (terminal)
        {
            case "Succeeded":
                Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId!.Value, lease.LeaseToken,
                    now.AddSeconds(2), new(CollectionAttemptResult.Succeeded)));
                break;
            case "Failed":
                Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId!.Value, lease.LeaseToken,
                    now.AddSeconds(2), new(CollectionAttemptResult.PermanentFailure,
                        FailureImpact: CollectionFailureImpact.Isolated)));
                break;
            case "Cancelled":
                Assert.IsTrue(await store.CancelTaskAsync(first.TaskId!.Value, now.AddSeconds(2)));
                Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId!.Value, lease.LeaseToken,
                    now.AddSeconds(3), new(CollectionAttemptResult.Cancelled)));
                break;
            case "DeadLetter":
                Assert.IsTrue(await store.ReconcileDeadLetterAsync(first.TaskId!.Value, 1, now.AddSeconds(2)));
                break;
        }

        var tasks = await store.GetTasksAsync(limit: 10);
        Assert.HasCount(2, tasks);
        var followUp = tasks.Single(x => x.TaskId != first.TaskId!.Value);
        Assert.AreEqual(2, followUp.RequestedRevision);
        Assert.AreEqual(CollectionTaskStatus.Ready, followUp.Status);
        Assert.AreEqual(CollectionLane.Background, followUp.Lane);
        Assert.AreEqual((int)CollectionPriority.Background, followUp.Priority);
        await store.SetPausedAsync(false, null, now.AddSeconds(4));
        var followUpLease = await store.AcquireAsync(followUp.TaskId, 1, now.AddMinutes(1), TimeSpan.FromMinutes(5));
        Assert.AreEqual("new-revision", followUpLease!.Attributes["name"]);
    }

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "collection-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task SameResourceAndRevision_CanBeCollectedMultipleTimesAfterCompletion()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId!.Value, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded)));
        var second = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now.AddDays(1));

        Assert.IsTrue(second.CreatedTask);
        Assert.AreNotEqual(first.TaskId!.Value, second.TaskId!.Value);
        var state = await store.GetStateAsync(Horse, HorseProfile);
        Assert.IsNotNull(state);
        Assert.AreEqual(7, state.AppliedRevision);
        Assert.AreEqual(CollectionStateStatus.Pending, state.Status);
    }

    [TestMethod]
    [DataRow(CollectionReason.Initial)]
    [DataRow(CollectionReason.Backfill)]
    [DataRow(CollectionReason.Discovery)]
    public async Task OrdinaryRegistration_ReusesExistingRevisionWithoutAddingHistoryOrDispatch(
        CollectionReason reason)
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId!.Value, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded)));
        var dispatchCountBeforeDuplicate = (await store.GetPendingDispatchesAsync(now.AddDays(1), 10)).Count;

        var duplicate = await store.RequestAsync(Horse, HorseProfile, 7, reason, now.AddDays(1),
            attributes: new Dictionary<string, string> { ["source"] = "rediscovered" });

        Assert.IsFalse(duplicate.CreatedTask);
        Assert.AreEqual(first.RequestId, duplicate.RequestId);
        Assert.AreEqual(first.TaskId!.Value, duplicate.TaskId!.Value);
        Assert.HasCount(1, await store.GetTasksAsync());
        var detail = await store.GetResourceDetailAsync(Horse, HorseProfile);
        Assert.IsNotNull(detail);
        Assert.HasCount(1, detail.Requests);
        Assert.AreEqual(dispatchCountBeforeDuplicate,
            (await store.GetPendingDispatchesAsync(now.AddDays(1), 10)).Count);
    }

    [TestMethod]
    public async Task OrdinaryRegistration_ReusedTaskMergesLaterExplicitRaceResultLocation()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 2, "combined", false);
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260920:Nakayama:6");
        var now = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
        var cardUrl = new Uri("https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604060620260920/0D");
        var resultUrl = new Uri("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde0106202604060620260920/C9");

        var first = await store.RequestAsync(resource, definition, 2, CollectionReason.Discovery, now,
            explicitUrl: cardUrl);
        var reused = await store.RequestAsync(resource, definition, 2, CollectionReason.Discovery,
            now.AddMinutes(1), explicitUrl: resultUrl);

        Assert.IsFalse(reused.CreatedTask);
        Assert.AreEqual(first.TaskId!.Value, reused.TaskId!.Value);
        var locations = await store.ResolveLocationsAsync(resource, definition);
        Assert.HasCount(2, locations);
        Assert.AreEqual(RaceArtifactKind.Card, locations.Single(x => x.Url == cardUrl).Artifact);
        Assert.AreEqual(RaceArtifactKind.Result, locations.Single(x => x.Url == resultUrl).Artifact);
    }

    [TestMethod]
    public async Task OrdinaryRegistration_CreatesTaskForANewerRevision()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId!.Value, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded)));
        await store.RegisterDefinitionAsync(HorseProfile, "Horse profile", ResourceType.Horse, 8,
            "Updated profile extractor", true);

        var updated = await store.RequestAsync(Horse, HorseProfile, 8, CollectionReason.Discovery, now.AddDays(1));

        Assert.IsTrue(updated.CreatedTask);
        Assert.AreNotEqual(first.TaskId!.Value, updated.TaskId!.Value);
        Assert.HasCount(2, await store.GetTasksAsync());
    }

    [TestMethod]
    [DataRow(CollectionReason.ManualRefresh)]
    [DataRow(CollectionReason.Recovery)]
    [DataRow(CollectionReason.ScheduledRefresh)]
    [DataRow(CollectionReason.DefinitionChanged)]
    public async Task RefreshReasons_CreateAnotherTaskForExistingRevision(CollectionReason reason)
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId!.Value, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded)));

        var refresh = await store.RequestAsync(Horse, HorseProfile, 7, reason, now.AddDays(1));

        Assert.IsTrue(refresh.CreatedTask);
        Assert.AreNotEqual(first.TaskId!.Value, refresh.TaskId!.Value);
        Assert.HasCount(2, await store.GetTasksAsync());
    }

    [TestMethod]
    public async Task ConcurrentOrdinaryRegistrations_CreateAtMostOneTask()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

        var receipts = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery,
                now.AddMilliseconds(index))));

        Assert.AreEqual(1, receipts.Count(x => x.CreatedTask));
        Assert.AreEqual(1, receipts.Select(x => x.TaskId).Distinct().Count());
        Assert.AreEqual(1, receipts.Select(x => x.RequestId).Distinct().Count());
        Assert.HasCount(1, await store.GetTasksAsync());
    }

    [TestMethod]
    public async Task SameResourceAndDefinition_ReusesActiveTaskButRetainsEachRequest()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;

        var initial = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var manual = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now.AddSeconds(1));

        Assert.IsTrue(initial.CreatedTask);
        Assert.IsFalse(manual.CreatedTask);
        Assert.AreEqual(initial.TaskId!.Value, manual.TaskId!.Value);
        Assert.AreNotEqual(initial.RequestId, manual.RequestId);
    }

    [TestMethod]
    public async Task Request_RejectsNonHttpExplicitUrl()
    {
        var store = await CreateStoreAsync();

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.RequestAsync(
            Horse, HorseProfile, 7, CollectionReason.ManualRefresh, DateTimeOffset.UtcNow,
            explicitUrl: new Uri("file:///JRADB/accessS.html")));

        Assert.AreEqual("explicitUrl", exception.ParamName);
        Assert.IsEmpty(await store.GetTasksAsync());
    }

    [TestMethod]
    public void HttpUrl_ResolvesRootRelativePathWithoutOperatingSystemFileSemantics()
    {
        var resolved = CollectionHttpUrl.Resolve("/JRADB/accessS.html?CNAME=result",
            "https://www.jra.go.jp/JRADB/accessD.html");

        Assert.AreEqual(new Uri("https://www.jra.go.jp/JRADB/accessS.html?CNAME=result"), resolved);
        Assert.IsFalse(CollectionHttpUrl.TryCreate("file:///JRADB/accessS.html", out _));
    }

    [TestMethod]
    public async Task Acquire_SkipsLegacyNonHttpExplicitUrl()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        await using (var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE collection_requests SET ExplicitUrl = $url";
            command.Parameters.AddWithValue("$url", "file:///JRADB/accessS.html");
            Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
        }

        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));

        Assert.IsNotNull(lease);
        Assert.IsEmpty(lease.Locations ?? []);
    }

    [TestMethod]
    public async Task SearchTasks_FiltersAndPagesWithAnExactTotalCount()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "H001"), HorseProfile, 7,
            CollectionReason.Initial, now, CollectionLane.Background, 10);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "H002"), HorseProfile, 7,
            CollectionReason.Initial, now.AddMinutes(1), CollectionLane.Normal, 50);
        await store.RequestAsync(new(ResourceType.Horse, "NAR", "H003"), HorseProfile, 7,
            CollectionReason.Initial, now.AddMinutes(2), CollectionLane.Normal, 50);

        var firstPage = await store.SearchTasksAsync(new(ResourceType: ResourceType.Horse,
            Provider: "jra", DefinitionId: "horse-profile", Page: 1, PageSize: 1));
        var secondPage = await store.SearchTasksAsync(new(ResourceType: ResourceType.Horse,
            Provider: "JRA", DefinitionId: "horse-profile", Page: 2, PageSize: 1));
        var searched = await store.SearchTasksAsync(new(Search: "H002"));
        var recent = await store.SearchTasksAsync(new(CreatedFrom: now.AddSeconds(30)));

        Assert.AreEqual(2, firstPage.TotalCount);
        Assert.HasCount(1, firstPage.Items);
        Assert.AreEqual("H002", firstPage.Items[0].Resource.Id);
        Assert.AreEqual("H001", secondPage.Items[0].Resource.Id);
        Assert.AreEqual(1, searched.TotalCount);
        Assert.AreEqual("H002", searched.Items.Single().Resource.Id);
        Assert.AreEqual(2, recent.TotalCount);
    }

    [TestMethod]
    public async Task OrdinaryRegistration_MergesActiveTaskToStrongerLaneAndPriorityWithoutChangingTaskId()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            CollectionLane.Background, (int)CollectionPriority.Background);

        var promoted = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery,
            now.AddMinutes(1), CollectionLane.Normal, (int)CollectionPriority.High);
        var lower = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery,
            now.AddMinutes(2), CollectionLane.Background, (int)CollectionPriority.Low);

        Assert.IsFalse(promoted.CreatedTask);
        Assert.AreEqual(first.TaskId!.Value, promoted.TaskId!.Value);
        Assert.AreEqual(first.TaskId!.Value, lower.TaskId!.Value);
        var task = (await store.GetTasksAsync()).Single();
        Assert.AreEqual(CollectionLane.Normal, task.Lane);
        Assert.AreEqual((int)CollectionPriority.High, task.Priority);
    }

    [TestMethod]
    public async Task OrdinaryRegistration_PromotesRunningTaskWithoutStartingAnotherAttempt()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now,
            CollectionLane.Background, (int)CollectionPriority.Background);
        var lease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        var promoted = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery,
            now.AddMinutes(1), CollectionLane.Realtime, (int)CollectionPriority.Critical);

        Assert.AreEqual(first.TaskId!.Value, promoted.TaskId!.Value);
        var task = (await store.GetTasksAsync()).Single();
        Assert.AreEqual(CollectionTaskStatus.Running, task.Status);
        Assert.AreEqual(CollectionLane.Realtime, task.Lane);
        Assert.AreEqual((int)CollectionPriority.Critical, task.Priority);
        Assert.HasCount(1, (await store.GetResourceDetailAsync(Horse, HorseProfile))!.Attempts);
    }

    [TestMethod]
    public async Task NotApplicableCompletesUnavailableWithoutActionableFailure()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Discovery, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken,
            now.AddMinutes(1), new(CollectionAttemptResult.NotApplicable,
                "SubjectNotInProviderDirectory", "提供元の公開名簿対象外です。")));

        var task = (await store.GetTasksAsync()).Single();
        Assert.AreEqual(CollectionTaskStatus.Succeeded, task.Status);
        Assert.AreEqual(CollectionStateStatus.Unavailable,
            (await store.GetStateAsync(Horse, HorseProfile))!.Status);
        Assert.IsEmpty(await store.GetActionableFailureNotificationsAsync(now.AddHours(1), 10));
    }

    [TestMethod]
    public async Task SearchLatestTasks_GroupsBeforeFilteringAndCountsTheLatestState()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        var failed = await store.RequestAsync(new(ResourceType.Horse, "JRA", "H001"), HorseProfile, 7,
            CollectionReason.Initial, now);
        var failedLease = await store.AcquireAsync(failed.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failed.TaskId!.Value, failedLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "OldFailure", "old failure only"));
        var latest = await store.RequestAsync(new(ResourceType.Horse, "JRA", "H001"), HorseProfile, 7,
            CollectionReason.ManualRefresh, now.AddMinutes(1));

        var allLatest = await store.SearchTasksAsync(new(LatestOnly: true));
        var failedLatest = await store.SearchTasksAsync(new(Statuses: [CollectionTaskStatus.Failed],
            LatestOnly: true));
        var oldErrorLatest = await store.SearchTasksAsync(new(ErrorSearch: "OldFailure", LatestOnly: true));
        var history = await store.SearchTasksAsync(new(Statuses: [CollectionTaskStatus.Failed]));
        var counts = await store.GetTaskViewCountsAsync();

        Assert.AreEqual(1, allLatest.TotalCount);
        Assert.AreEqual(latest.TaskId!.Value, allLatest.Items.Single().TaskId);
        Assert.AreEqual(CollectionTaskStatus.Ready, allLatest.Items.Single().Status);
        Assert.AreEqual(0, failedLatest.TotalCount);
        Assert.AreEqual(0, oldErrorLatest.TotalCount);
        Assert.AreEqual(1, history.TotalCount, "The superseded failed task remains available as history.");
        Assert.AreEqual(1, counts.Counts["waiting"]);
        Assert.AreEqual(0, counts.Counts["recent"]);
        Assert.AreEqual(1, counts.Counts["all"]);
    }

    [TestMethod]
    public async Task TaskViewCounts_RunningIncludesOnlyUnexpiredLeasesWithoutReclaimingTasks()
    {
        var store = await CreateStoreAsync();
        var now = HorseRacingPrediction.Contracts.Time.JstTime.Now();
        var expired = await store.RequestAsync(new(ResourceType.Horse, "JRA", "EXPIRED"),
            HorseProfile, 7, CollectionReason.Initial, now.AddMinutes(-10));
        var active = await store.RequestAsync(new(ResourceType.Horse, "JRA", "ACTIVE"),
            HorseProfile, 7, CollectionReason.Initial, now);
        Assert.IsNotNull(await store.AcquireAsync(active.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5)));
        Assert.IsNotNull(await store.AcquireAsync(expired.TaskId!.Value, 1, now.AddMinutes(-10), TimeSpan.FromMinutes(1)));

        var counts = await store.GetTaskViewCountsAsync();
        await using var db = new CollectionPlatformDbContext(
            new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False")
                .Options);
        var expiredTask = await db.Tasks.AsNoTracking().SingleAsync(x => x.TaskId == expired.TaskId!.Value);
        var expiredAttempt = await db.Attempts.AsNoTracking().SingleAsync(x => x.TaskId == expired.TaskId!.Value);

        Assert.AreEqual(1, counts.Counts["running"]);
        Assert.AreEqual(CollectionTaskStatus.Running, expiredTask.Status);
        Assert.AreEqual(CollectionAttemptResult.Running, expiredAttempt.Result);
    }

    [TestMethod]
    public async Task SearchTasks_OrdersIncompleteBeforeSucceeded_ThenLanePriorityAndDefinitionAcrossPages()
    {
        var store = await CreateStoreAsync();
        var alpha = new CollectionDefinitionId("alpha-profile");
        var beta = new CollectionDefinitionId("beta-profile");
        await store.RegisterDefinitionAsync(alpha, "Alpha", ResourceType.Horse, 1, "initial", false);
        await store.RegisterDefinitionAsync(beta, "Beta", ResourceType.Horse, 1, "initial", false);
        var now = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);

        var completedRealtime = await store.RequestAsync(new(ResourceType.Horse, "JRA", "COMPLETED"), beta, 1,
            CollectionReason.Initial, now, CollectionLane.Realtime, 100);
        await CompleteAsync(store, completedRealtime, now);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "BACKGROUND"), alpha, 1,
            CollectionReason.Initial, now.AddMinutes(1), CollectionLane.Background, 100);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "NORMAL"), alpha, 1,
            CollectionReason.Initial, now.AddMinutes(2), CollectionLane.Normal, 100);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "REALTIME-LOW"), alpha, 1,
            CollectionReason.Initial, now.AddMinutes(3), CollectionLane.Realtime, 80);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "REALTIME-BETA"), beta, 1,
            CollectionReason.Initial, now.AddMinutes(4), CollectionLane.Realtime, 90);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "REALTIME-ALPHA"), alpha, 1,
            CollectionReason.Initial, now.AddMinutes(5), CollectionLane.Realtime, 90);

        var firstPage = await store.SearchTasksAsync(new(Page: 1, PageSize: 3));
        var secondPage = await store.SearchTasksAsync(new(Page: 2, PageSize: 3));
        var createdFrom = await store.SearchTasksAsync(new(CreatedFrom: now, Page: 1, PageSize: 10));
        var expected = new[]
        {
            "REALTIME-ALPHA", "REALTIME-BETA", "REALTIME-LOW", "NORMAL", "BACKGROUND", "COMPLETED",
        };

        Assert.AreEqual(6, firstPage.TotalCount);
        CollectionAssert.AreEqual(expected[..3], firstPage.Items.Select(x => x.Resource.Id).ToArray());
        CollectionAssert.AreEqual(expected[3..], secondPage.Items.Select(x => x.Resource.Id).ToArray());
        CollectionAssert.AreEqual(expected, createdFrom.Items.Select(x => x.Resource.Id).ToArray());
    }

    [TestMethod]
    public async Task Request_RejectsRevisionNotRegisteredByDefinition()
    {
        var store = await CreateStoreAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.RequestAsync(
            Horse, HorseProfile, 999, CollectionReason.ManualRefresh, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task TransientFailure_RetriesSameTaskAndPreservesAttemptHistoryAcrossRestart()
    {
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var first = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(first);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, first.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.TransientFailure, "Http503", HttpStatusCode: 503,
                RetryAt: now.AddMinutes(2))));

        var restarted = CreateStore();
        var second = await restarted.AcquireAsync(receipt.TaskId!.Value, 2, now.AddMinutes(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(second);
        Assert.IsTrue(await restarted.CompleteAttemptAsync(receipt.TaskId!.Value, second.LeaseToken, now.AddMinutes(3),
            new(CollectionAttemptResult.Succeeded, RequestedUrl: new("https://example.test/horse/H123"),
                PageIdentification: "Horse:JRA:H123")));

        var attempts = await restarted.GetAttemptsAsync(receipt.TaskId!.Value);
        Assert.HasCount(2, attempts);
        Assert.AreEqual(CollectionAttemptResult.TransientFailure, attempts[0].Result);
        Assert.AreEqual(503, attempts[0].HttpStatusCode);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, attempts[1].Result);
        Assert.AreEqual("Horse:JRA:H123", attempts[1].PageIdentification);
    }

    [TestMethod]
    public async Task ExpiredLease_IsRecoveredWithNewDispatchGeneration()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery, now);
        Assert.IsNotNull(await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(1)));

        var restarted = CreateStore();
        Assert.AreEqual(1, await restarted.ReclaimExpiredLeasesAsync(now.AddMinutes(2)));
        var recovered = await restarted.AcquireAsync(receipt.TaskId!.Value, 2, now.AddMinutes(2), TimeSpan.FromMinutes(1));

        Assert.IsNotNull(recovered);
        var attempts = await store.GetAttemptsAsync(receipt.TaskId!.Value);
        Assert.HasCount(2, attempts);
        Assert.AreEqual("LeaseExpired", attempts[0].ErrorCode);
    }

    [TestMethod]
    public async Task RevisionImpact_UpdatesOnlyMatchingResources()
    {
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var store = await CreateStoreAsync();
        var other = new ResourceKey(ResourceType.Horse, "JRA", "H999");
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now,
            effectiveDate: new DateOnly(2009, 1, 1), attributes: new Dictionary<string, string> { ["layout"] = "legacy" });
        var second = await store.RequestAsync(other, HorseProfile, 7, CollectionReason.Initial, now,
            effectiveDate: new DateOnly(2020, 1, 1), attributes: new Dictionary<string, string> { ["layout"] = "current" });
        await CompleteAsync(store, first, now);
        await CompleteAsync(store, second, now);

        var affected = await store.AddRevisionAndApplyImpactAsync(HorseProfile, 8, "Legacy layout fix",
            new(RevisionImpactScopeType.NamedCondition, "horse-profile:legacy-layout"),
            [new LegacyLayoutCondition()], now.AddDays(1));

        Assert.AreEqual(1, affected);
        var stale = await store.GetStateAsync(Horse, HorseProfile);
        var current = await store.GetStateAsync(other, HorseProfile);
        Assert.AreEqual(CollectionStateStatus.Stale, stale!.Status);
        Assert.AreEqual(8, stale.RequiredRevision);
        Assert.AreEqual(CollectionStateStatus.Current, current!.Status);
        Assert.AreEqual(7, current.RequiredRevision);
    }

    [TestMethod]
    public async Task RevisionImpact_RejectsUnknownNamedCondition()
    {
        var store = await CreateStoreAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.AddRevisionAndApplyImpactAsync(
            HorseProfile, 8, "Unknown selector", new(RevisionImpactScopeType.NamedCondition, "unknown"),
            [], DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task TransientLocationFailure_DoesNotInvalidateLocation()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var id = await store.UpsertLocationAsync(Horse, HorseProfile, new("https://example.test/horse/H123"),
            ResourceLocationSource.Discovered, now);

        await store.RecordLocationOutcomeAsync(id, CollectionAttemptResult.TransientFailure, now.AddMinutes(1), "Http503");

        var locations = await store.ResolveLocationsAsync(Horse, HorseProfile);
        Assert.HasCount(1, locations);
        Assert.AreEqual(ResourceLocationStatus.Unknown, locations[0].Status);
    }

    [TestMethod]
    public async Task SuccessfulRepeatedObservation_BecomesDueAtNextCollectionTime()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        var next = now.AddMinutes(10);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded, NextCollectionAt: next)));

        Assert.IsEmpty(await store.GetDueStatesAsync(next.AddTicks(-1)));
        var due = await store.GetDueStatesAsync(next);
        Assert.HasCount(1, due);
        Assert.AreEqual(Horse.Normalize(), due[0].Resource);
    }

    [TestMethod]
    public async Task SuccessfulExplicitUrl_IsPromotedToVerifiedResourceLocation()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var url = new Uri("https://example.test/horse/H123");
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now,
            explicitUrl: url);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.AreEqual(url, lease.Locations![0].Url);
        Assert.AreEqual(0, lease.Locations[0].LocationId);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, RequestedUrl: url, FinalUrl: url,
                LocationOutcomes: [new(lease.Locations[0].LocationId, CollectionAttemptResult.Succeeded)])));

        var locations = await store.ResolveLocationsAsync(Horse, HorseProfile);
        Assert.HasCount(1, locations);
        Assert.AreEqual(ResourceLocationStatus.Active, locations[0].Status);
        Assert.IsNotNull(locations[0].LastVerifiedAt);
    }

    [TestMethod]
    public async Task UnexpectedPage_MarksLocationSuspectAndSuccessVerifiesIt()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var id = await store.UpsertLocationAsync(Horse, HorseProfile, new("https://example.test/horse/H123"),
            ResourceLocationSource.Generated, now);

        await store.RecordLocationOutcomeAsync(id, CollectionAttemptResult.UnexpectedPage, now.AddMinutes(1));
        Assert.AreEqual(ResourceLocationStatus.Suspect, (await store.ResolveLocationsAsync(Horse, HorseProfile))[0].Status);
        await store.RecordLocationOutcomeAsync(id, CollectionAttemptResult.Succeeded, now.AddMinutes(2));
        Assert.AreEqual(ResourceLocationStatus.Active, (await store.ResolveLocationsAsync(Horse, HorseProfile))[0].Status);
    }

    [TestMethod]
    public async Task CompleteAttempt_RecordsEveryCandidateOutcomeAtomically()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore();
        await store.RegisterDefinitionAsync(new("race-card"), "Race card", ResourceType.RaceCard, 1,
            "Initial", false);
        var resource = new ResourceKey(ResourceType.RaceCard, "JRA", "R1");
        var receipt = await store.RequestAsync(resource, new("race-card"), 1, CollectionReason.Initial, now,
            CollectionLane.Realtime, 80);
        var first = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/wrong"), ResourceLocationSource.Discovered, now);
        var second = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/card"), ResourceLocationSource.Discovered, now);
        var transient = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/temporarily-unavailable"), ResourceLocationSource.Discovered, now);
        var limited = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/rate-limited"), ResourceLocationSource.Discovered, now);
        var missing = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/missing"), ResourceLocationSource.Discovered, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [
                new(first, CollectionAttemptResult.UnexpectedPage, "RaceIdMismatch"),
                new(transient, CollectionAttemptResult.TransientFailure, "Http503"),
                new(limited, CollectionAttemptResult.AccessLimited, "Http429"),
                new(missing, CollectionAttemptResult.ResourceNotFound, "Http404"),
                new(second, CollectionAttemptResult.Succeeded),
            ])));

        var locations = await store.ResolveLocationsAsync(resource, new("race-card"));
        Assert.AreEqual(ResourceLocationStatus.Active, locations.Single(x => x.LocationId == second).Status);
        Assert.AreEqual(ResourceLocationStatus.Suspect, locations.Single(x => x.LocationId == first).Status);
        Assert.AreEqual(ResourceLocationStatus.Unknown, locations.Single(x => x.LocationId == transient).Status);
        Assert.AreEqual(ResourceLocationStatus.Unknown, locations.Single(x => x.LocationId == limited).Status);
        Assert.AreEqual(ResourceLocationStatus.Suspect, locations.Single(x => x.LocationId == missing).Status);
    }

    [TestMethod]
    public async Task CompleteAttempt_RejectsForeignOrMissingLocationWithoutChangingTaskOrLocations()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-card");
        await store.RegisterDefinitionAsync(definition, "Race card", ResourceType.RaceCard, 1,
            "Initial", false);
        var resource = new ResourceKey(ResourceType.RaceCard, "JRA", "R1");
        var foreignResource = new ResourceKey(ResourceType.RaceCard, "JRA", "R2");
        var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial, now);
        await store.RequestAsync(foreignResource, definition, 1, CollectionReason.Initial, now);
        var own = await store.UpsertLocationAsync(resource, definition, new("https://example.test/r1"),
            ResourceLocationSource.Discovered, now);
        var foreign = await store.UpsertLocationAsync(foreignResource, definition, new("https://example.test/r2"),
            ResourceLocationSource.Discovered, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [new(own, CollectionAttemptResult.Succeeded), new(foreign, CollectionAttemptResult.UnexpectedPage)])));
        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(2),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [new(own, CollectionAttemptResult.Succeeded), new(long.MaxValue, CollectionAttemptResult.UnexpectedPage)])));

        Assert.AreEqual(ResourceLocationStatus.Unknown,
            (await store.ResolveLocationsAsync(resource, definition)).Single().Status);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.Succeeded,
                LocationOutcomes: [new(own, CollectionAttemptResult.Succeeded)])));
    }

    [TestMethod]
    public async Task CompleteAttempt_RejectsConflictingOrExcessiveOutcomesButAcceptsExactDuplicates()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var location = await store.UpsertLocationAsync(Horse, HorseProfile,
            new("https://example.test/horse/H123"), ResourceLocationSource.Discovered, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [
                new(location, CollectionAttemptResult.Succeeded),
                new(location, CollectionAttemptResult.UnexpectedPage, "RaceIdMismatch"),
            ])));
        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(2),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes: Enumerable.Range(0, 101)
                .Select(_ => new ResourceLocationOutcome(location, CollectionAttemptResult.Succeeded)).ToList())));

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [
                new(location, CollectionAttemptResult.Succeeded),
                new(location, CollectionAttemptResult.Succeeded),
            ])));
        Assert.AreEqual(ResourceLocationStatus.Active,
            (await store.ResolveLocationsAsync(Horse, HorseProfile)).Single().Status);
    }

    [TestMethod]
    public async Task CompleteAttempt_WrongLeaseAndRedeliveryCannotOverwriteCompletedOutcome()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var location = await store.UpsertLocationAsync(Horse, HorseProfile,
            new("https://example.test/horse/H123"), ResourceLocationSource.Discovered, now);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsFalse(await store.CompleteAttemptAsync(taskId, "wrong-lease", now.AddSeconds(1),
            new(CollectionAttemptResult.UnexpectedPage,
                LocationOutcomes: [new(location, CollectionAttemptResult.UnexpectedPage)])));
        Assert.IsTrue(await store.CompleteAttemptAsync(taskId, lease.LeaseToken, now.AddSeconds(2),
            new(CollectionAttemptResult.Succeeded,
                LocationOutcomes: [new(location, CollectionAttemptResult.Succeeded)])));
        Assert.IsFalse(await store.CompleteAttemptAsync(taskId, lease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.UnexpectedPage,
                LocationOutcomes: [new(location, CollectionAttemptResult.UnexpectedPage)])));

        var state = await store.GetStateAsync(Horse, HorseProfile);
        Assert.AreEqual(CollectionStateStatus.Current, state?.Status);
        Assert.AreEqual(ResourceLocationStatus.Active,
            (await store.ResolveLocationsAsync(Horse, HorseProfile)).Single().Status);
    }

    [TestMethod]
    public async Task Startup_BaselinesEnsureCreatedDatabaseWithoutLosingExistingData()
    {
        var databasePath = Path.Combine(_directory, "collection-platform.db");
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        await using (var legacy = new CollectionPlatformDbContext(options))
        {
            await legacy.Database.EnsureCreatedAsync();
            legacy.Definitions.Add(new CollectionDefinitionEntity
            {
                DefinitionId = HorseProfile.Value,
                Name = "Existing definition",
                ResourceType = ResourceType.Horse,
                CurrentRevision = 7,
                Enabled = true
            });
            legacy.Revisions.Add(new CollectionRevisionEntity
            {
                DefinitionId = HorseProfile.Value,
                Revision = 7,
                Description = "Existing revision",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await legacy.SaveChangesAsync();
        }

        _ = CreateStore();

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var history = connection.CreateCommand();
        history.CommandText = "SELECT MAX(version) FROM collection_schema_history;";
        Assert.AreEqual(18L, (long)(await history.ExecuteScalarAsync())!);
        await using var existing = connection.CreateCommand();
        existing.CommandText = "SELECT Name FROM collection_definitions WHERE DefinitionId = 'horse-profile';";
        Assert.AreEqual("Existing definition", await existing.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task Startup_RejectsIncompleteUnversionedDatabase()
    {
        var databasePath = Path.Combine(_directory, "collection-platform.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE collection_resources (ResourcePk INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => CreateStore());
        StringAssert.Contains(error.Message, "incomplete schema");
    }

    [TestMethod]
    public async Task TransientFailure_WithoutExplicitRetryAt_UsesDefaultBackoff()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsTrue(await store.CompleteAttemptAsync(taskId, lease.LeaseToken, now,
            new(CollectionAttemptResult.TransientFailure, "Http503")));

        var task = (await store.GetTasksAsync()).Single(x => x.TaskId == taskId);
        Assert.AreEqual(CollectionTaskStatus.Ready, task.Status);
        Assert.IsTrue(task.AvailableAt > now);
    }

    [TestMethod]
    public async Task Pause_PersistsAcrossRestartAndBlocksAcquisitionUntilResume()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        await store.SetPausedAsync(true, "maintenance", now);

        var restarted = CreateStore();
        Assert.IsTrue((await restarted.GetPipelineStateAsync()).IsPaused);
        Assert.IsNull(await restarted.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5)));

        await restarted.SetPausedAsync(false, null, now.AddMinutes(1));
        Assert.IsNotNull(await restarted.AcquireAsync(taskId, 1, now.AddMinutes(1), TimeSpan.FromMinutes(5)));
    }

    [TestMethod]
    public async Task CancelPendingTask_RemovesActiveGuardAndSuppressesOutbox()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);

        Assert.IsTrue(await store.CancelTaskAsync(receipt.TaskId!.Value, now.AddSeconds(1)));

        Assert.AreEqual(CollectionTaskStatus.Cancelled,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId!.Value).Status);
        Assert.IsFalse(await store.HasActiveTaskAsync(Horse, HorseProfile));
        Assert.IsEmpty(await store.GetPendingDispatchesAsync(now.AddMinutes(1), 10));
    }

    [TestMethod]
    public async Task DeadLetter_CurrentGenerationFailsTaskAndQueuesNotification()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);

        Assert.IsFalse(await store.ReconcileDeadLetterAsync(receipt.TaskId!.Value, 0, now));
        Assert.IsTrue(await store.ReconcileDeadLetterAsync(receipt.TaskId!.Value, 1, now, "lambda failed"));

        Assert.AreEqual(CollectionTaskStatus.DeadLetter,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId!.Value).Status);
        var notifications = await store.GetPendingFailureNotificationsAsync(now.AddSeconds(1), 10);
        Assert.HasCount(1, notifications);
        Assert.AreEqual("DeadLetterQueue", notifications[0].ErrorCode);
        var pipeline = await store.GetPipelineStateAsync();
        Assert.IsTrue(pipeline.IsPaused);
        StringAssert.Contains(pipeline.Reason, notifications[0].NotificationId.ToString("D"));
    }

    [TestMethod]
    public async Task ResourceNotFound_IsUnavailableWithoutPausingPipeline()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified")));

        Assert.IsFalse((await store.GetPipelineStateAsync()).IsPaused);
        Assert.AreEqual(CollectionStateStatus.Unavailable, (await store.GetStateAsync(Horse, HorseProfile))!.Status);
    }

    [TestMethod]
    public async Task Recovery_RepairsTerminalActiveReferenceAndCreatesFreshTask()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var failed = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now,
            attributes: new Dictionary<string, string> { ["name"] = "A" });
        var lease = await store.AcquireAsync(failed.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(failed.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "Broken"));
        await store.SetPausedAsync(false, null, now.AddSeconds(2));
        await using (var connection = new SqliteConnection(
                         $"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO collection_active_tasks (ResourcePk, DefinitionId, TaskId)
                SELECT ResourcePk, DefinitionId, TaskId FROM collection_tasks WHERE TaskId = $taskId;
                """;
            command.Parameters.AddWithValue("$taskId", failed.TaskId!.Value.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery,
            now.AddMinutes(1));

        Assert.IsTrue(recovery.CreatedTask);
        Assert.AreNotEqual(failed.TaskId!.Value, recovery.TaskId!.Value);
        Assert.IsNotNull(await store.AcquireAsync(recovery.TaskId!.Value, 1, now.AddMinutes(1), TimeSpan.FromMinutes(5)));
    }

    [TestMethod]
    public async Task Watchdog_DoesNotRedispatchReadyTaskThatMayStillBeWaitingInQueue()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var firstOutbox = (await store.GetPendingDispatchesAsync(now, 10)).Single();
        await store.MarkDispatchedAsync(firstOutbox.OutboxId, now);

        var recovered = await store.RunWatchdogAsync(now.AddHours(2), 2, TimeSpan.FromMinutes(1));

        Assert.AreEqual(0, recovered.RedispatchedTasks);
        Assert.AreEqual(0, recovered.DeadLetteredTasks);
        Assert.IsEmpty(await store.GetPendingDispatchesAsync(now.AddHours(2), 10));
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId!.Value).Status);
    }

    [TestMethod]
    public async Task Backfill_RestartsWithoutDuplicateTasksAndProjectsHolesWhileLaterBatchContinues()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
            1, "initial", false);
        var first = await store.CreateOrResumeBackfillBatchAsync("jra:2026-01", "jra",
            new(2026, 1, 1), new(2026, 1, 3), now);
        Assert.AreEqual(3, first.RegisteredDiscoveryDays);

        var tasks = await store.GetTasksAsync();
        var day1 = tasks.Single(x => x.Resource.Id == "backfill:20260101");
        var failedLease = await store.AcquireAsync(day1.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(day1.TaskId, failedLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "BrokenDay", "fixture failure"));
        await store.SetPausedAsync(false, null, now.AddSeconds(2));
        var day2 = tasks.Single(x => x.Resource.Id == "backfill:20260102");
        var successLease = await store.AcquireAsync(day2.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(successLease);
        await store.CompleteAttemptAsync(day2.TaskId, successLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded));

        var restarted = CreateStore();
        var resumed = await restarted.CreateOrResumeBackfillBatchAsync("jra:2026-01", "jra",
            new(2026, 1, 1), new(2026, 1, 3), now.AddMinutes(1));
        Assert.AreEqual(3, resumed.RegisteredDiscoveryDays);
        Assert.AreEqual(1, resumed.Failed);
        Assert.AreEqual(1, resumed.Succeeded);
        Assert.AreEqual(1, resumed.Pending);
        Assert.HasCount(1, resumed.Holes);
        Assert.AreEqual("backfill:20260101", resumed.Holes[0].Resource.Id);
        Assert.AreEqual("BrokenDay", resumed.Holes[0].ErrorCode);

        var later = await restarted.CreateOrResumeBackfillBatchAsync("jra:2026-02", "jra",
            new(2026, 2, 1), new(2026, 2, 2), now.AddMinutes(2));
        Assert.AreEqual(2, later.RegisteredDiscoveryDays);
        Assert.AreEqual(2, later.Pending);
    }

    [TestMethod]
    public async Task Backfill_ResolvedRecoveryIsRemovedFromProjectedHoles()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var discovery = new CollectionDefinitionId("race-discovery");
        await store.RegisterDefinitionAsync(discovery, "Race discovery", ResourceType.Race,
            1, "initial", false);
        await store.CreateOrResumeBackfillBatchAsync("jra:recovery", "jra",
            new(2026, 1, 1), new(2026, 1, 1), now);
        var failedTask = (await store.GetTasksAsync()).Single(x => x.Resource.Id == "backfill:20260101");
        var failedLease = await store.AcquireAsync(failedTask.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failedTask.TaskId, failedLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "BrokenDay"));
        await store.SetPausedAsync(false, null, now.AddSeconds(2));
        Assert.HasCount(1, (await store.GetBackfillBatchAsync("jra:recovery"))!.Holes);

        var recovery = await store.RequestAsync(failedTask.Resource, discovery, 1, CollectionReason.Recovery,
            now.AddMinutes(1), CollectionLane.Background, (int)CollectionPriority.Background,
            batchId: "recovery:jra:recovery");
        await CompleteAsync(store, recovery, now.AddMinutes(1));

        var recovered = await store.GetBackfillBatchAsync("jra:recovery");
        Assert.IsNotNull(recovered);
        Assert.AreEqual(0, recovered.Failed);
        Assert.IsEmpty(recovered.Holes);
    }

    [TestMethod]
    public async Task Backfill_RecoveryAtSameTimestampStillResolvesHole()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore();
        var discovery = new CollectionDefinitionId("race-discovery");
        await store.RegisterDefinitionAsync(discovery, "Race discovery", ResourceType.Race,
            1, "initial", false);
        await store.CreateOrResumeBackfillBatchAsync("jra:same-time", "jra",
            new(2026, 1, 1), new(2026, 1, 1), now);
        var failedTask = (await store.GetTasksAsync()).Single();
        var failedLease = await store.AcquireAsync(failedTask.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failedTask.TaskId, failedLease.LeaseToken, now,
            new(CollectionAttemptResult.PermanentFailure, "BrokenDay"));
        await store.SetPausedAsync(false, null, now.AddSeconds(1));

        var recovery = await store.RequestAsync(failedTask.Resource, discovery, 1, CollectionReason.Recovery,
            now, CollectionLane.Background, (int)CollectionPriority.Background,
            batchId: "recovery:jra:same-time:1");
        var recoveryLease = await store.AcquireAsync(recovery.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(recoveryLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(recovery.TaskId!.Value, recoveryLease.LeaseToken, now,
            new(CollectionAttemptResult.Succeeded)));

        Assert.IsEmpty((await store.GetBackfillBatchAsync("jra:same-time"))!.Holes);
    }

    [TestMethod]
    public async Task Backfill_OlderRecoverySuccessDoesNotHideLaterFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore();
        var discovery = new CollectionDefinitionId("race-discovery");
        await store.RegisterDefinitionAsync(discovery, "Race discovery", ResourceType.Race,
            1, "initial", false);
        await store.CreateOrResumeBackfillBatchAsync("jra:ordered", "jra",
            new(2026, 1, 1), new(2026, 1, 1), now);
        var failedTask = (await store.GetTasksAsync()).Single();
        var failedLease = await store.AcquireAsync(failedTask.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failedTask.TaskId, failedLease.LeaseToken, now,
            new(CollectionAttemptResult.PermanentFailure, "BrokenDay"));
        await store.SetPausedAsync(false, null, now.AddSeconds(1));

        var recovery = await store.RequestAsync(failedTask.Resource, discovery, 1, CollectionReason.Recovery,
            now.AddMinutes(-1), CollectionLane.Background, (int)CollectionPriority.Background,
            batchId: "recovery:jra:ordered:old");
        var recoveryLease = await store.AcquireAsync(recovery.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(recoveryLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(recovery.TaskId!.Value, recoveryLease.LeaseToken, now,
            new(CollectionAttemptResult.Succeeded)));

        Assert.HasCount(1, (await store.GetBackfillBatchAsync("jra:ordered"))!.Holes);
    }

    [TestMethod]
    public async Task BackfillList_OrdersBatchesWithoutSqliteDateTimeOffsetOrdering()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
            1, "Initial", false);
        await store.CreateOrResumeBackfillBatchAsync("older", "jra", new(2026, 1, 1), new(2026, 1, 1),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await store.CreateOrResumeBackfillBatchAsync("newer", "jra", new(2026, 2, 1), new(2026, 2, 1),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var batches = await store.GetBackfillBatchesAsync();

        CollectionAssert.AreEqual(new[] { "newer", "older" }, batches.Select(x => x.BatchId).ToArray());
    }

    [TestMethod]
    public async Task Startup_ConcurrentStores_SerializeSchemaInitialization()
    {
        var stores = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(CreateStore)));

        Assert.HasCount(8, stores);
        await using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM collection_schema_history WHERE version = 18;";
        Assert.AreEqual(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task Startup_Version7ConvertsExistingUtcValuesToTimezoneLessJst()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(HorseProfile, "Horse", ResourceType.Horse, 1, "initial", false);
        await store.RequestAsync(Horse, HorseProfile, 1, CollectionReason.Initial,
            new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.Zero));
        var databasePath = Path.Combine(_directory, "collection-platform.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var downgrade = connection.CreateCommand();
            downgrade.CommandText = """
                DELETE FROM collection_schema_history WHERE version >= 7;
                INSERT INTO collection_schema_history (version, applied_at) VALUES (6, '2026-09-13T15:00:00+00:00');
                UPDATE collection_tasks SET CreatedAt = '2026-09-13T15:30:00.0000000+00:00';
                """;
            await downgrade.ExecuteNonQueryAsync();
        }

        _ = CreateStore();

        await using var verification = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await verification.OpenAsync();
        await using var command = verification.CreateCommand();
        command.CommandText = "SELECT CreatedAt FROM collection_tasks LIMIT 1;";
        Assert.AreEqual("2026-09-14 00:30:00.000", await command.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task ResourceDetail_ReturnsStateLocationsRequestsTasksAndAttempts()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now,
            explicitUrl: new Uri("https://example.test/horse/H123"));
        await store.UpsertLocationAsync(Horse, HorseProfile, new Uri("https://example.test/horse/H123"),
            ResourceLocationSource.Manual, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded, RequestedUrl: new("https://example.test/horse/H123"),
                PageIdentification: "Horse:JRA:H123")));

        var detail = await store.GetResourceDetailAsync(Horse, HorseProfile);

        Assert.IsNotNull(detail);
        Assert.IsNotNull(detail.State);
        Assert.HasCount(1, detail.Locations);
        Assert.HasCount(1, detail.Requests);
        Assert.HasCount(1, detail.Tasks);
        Assert.HasCount(1, detail.Attempts);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, detail.Attempts[0].Result);
        Assert.AreEqual("Horse:JRA:H123", detail.Attempts[0].PageIdentification);
    }

    [TestMethod]
    public async Task AttemptCorrelation_IsPersistedAndListsEveryTaskInExecutionBatch()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var otherHorse = new ResourceKey(ResourceType.Horse, "JRA", "H456");
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var second = await store.RequestAsync(otherHorse, HorseProfile, 7, CollectionReason.Initial, now);
        var executionBatchId = Guid.NewGuid();
        var envelopeId = Guid.NewGuid();

        var firstLease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5),
            new CollectionAttemptCorrelation(executionBatchId, envelopeId, "sqs-123", "lambda-456", 1, 2));
        var secondLease = await store.AcquireAsync(second.TaskId!.Value, 1, now.AddSeconds(1), TimeSpan.FromMinutes(5),
            new CollectionAttemptCorrelation(executionBatchId, envelopeId, "sqs-123", "lambda-456", 2, 2));
        Assert.IsNotNull(firstLease);
        Assert.IsNotNull(secondLease);
        await store.CompleteAttemptAsync(first.TaskId!.Value, firstLease.LeaseToken, now.AddSeconds(2),
            new(CollectionAttemptResult.Succeeded));
        await store.CompleteAttemptAsync(second.TaskId!.Value, secondLease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.ParseFailure, "Parser"));

        var detail = await store.GetResourceDetailAsync(Horse, HorseProfile);
        var attempt = detail!.Attempts.Single();
        Assert.AreEqual(executionBatchId, attempt.ExecutionBatchId);
        Assert.AreEqual(envelopeId, attempt.DispatchEnvelopeId);
        Assert.AreEqual("sqs-123", attempt.QueueMessageId);
        Assert.AreEqual("lambda-456", attempt.LambdaRequestId);
        Assert.AreEqual(1, attempt.BatchTaskOrdinal);
        Assert.AreEqual(2, attempt.BatchTaskCount);

        var batch = await store.GetExecutionBatchAsync(executionBatchId);
        Assert.IsNotNull(batch);
        Assert.AreEqual(2, batch.BatchTaskCount);
        Assert.HasCount(2, batch.Tasks);
        CollectionAssert.AreEqual(new[] { "H123", "H456" }, batch.Tasks.Select(x => x.Resource.Id).ToArray());
        CollectionAssert.AreEqual(new[] { CollectionAttemptResult.Succeeded, CollectionAttemptResult.ParseFailure },
            batch.Tasks.Select(x => x.Result).ToArray());
    }

    [TestMethod]
    public async Task StatePagingAndTaskErrorSearch_AreAppliedBeforePaging()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 60; index++)
        {
            var resource = new ResourceKey(ResourceType.Horse, "JRA", $"H{index:D3}");
            var receipt = await store.RequestAsync(resource, HorseProfile, 7, CollectionReason.Initial, now);
            if (index == 55)
            {
                var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
                Assert.IsNotNull(lease);
                await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(1),
                    new(CollectionAttemptResult.ParseFailure, "DistinctParserError", "grade parse failed"));
            }
        }

        var states = await store.SearchStatesAsync(new(Page: 2, PageSize: 25));
        var errors = await store.SearchTasksAsync(new(ErrorSearch: "DistinctParserError", Page: 1, PageSize: 1));

        Assert.AreEqual(60, states.TotalCount);
        Assert.HasCount(25, states.Items);
        Assert.AreEqual(1, errors.TotalCount);
        Assert.AreEqual("H055", errors.Items[0].Resource.Id);
    }

    [TestMethod]
    public async Task RaceDetailCompletion_PersistsIndependentFacetsEvidenceAndStageHistory()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        var race = new ResourceKey(ResourceType.Race, "JRA", "20260919:Tokyo:10");
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 2, "facets", false);
        var now = new DateTimeOffset(2026, 9, 19, 1, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(race, definition, 2, CollectionReason.Initial, now,
            effectiveDate: new DateOnly(2026, 9, 19));
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        var start = now.AddHours(5);

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotYetAvailable, "ResultNotConfirmed", RetryAt: start,
                StageOutcomes:
                [
                    new("PersistCard", RaceArtifactKind.Card, CollectionAttemptResult.Succeeded, Persisted: true),
                    new("ConfirmResult", RaceArtifactKind.Result,
                        CollectionAttemptResult.ResourceNotYetAvailable, "ResultNotConfirmed"),
                ],
                RaceEvidence: new(start, "JRA-RaceCard", now))));

        var detail = await store.GetResourceDetailAsync(race, definition);
        Assert.IsNotNull(detail);
        var artifacts = detail.RaceArtifacts!;
        Assert.AreEqual(RaceArtifactStatus.Current,
            artifacts.Single(x => x.Artifact == RaceArtifactKind.Card).Status);
        Assert.AreEqual(RaceArtifactStatus.AwaitingPublication,
            artifacts.Single(x => x.Artifact == RaceArtifactKind.Result).Status);
        Assert.AreEqual(start, detail.RaceEvidence!.OfficialStartAt);
        Assert.HasCount(2, detail.StageOutcomes!);
        var monitoringRace = (await store.GetMonitoringSnapshotAsync(
            now.AddMinutes(1), now.AddDays(-1), 100)).RaceFreshness!.Single();
        Assert.AreEqual(RaceArtifactStatus.Current, monitoringRace.CardStatus);
        Assert.AreEqual(RaceArtifactStatus.AwaitingPublication, monitoringRace.ResultStatus);
        Assert.AreEqual(start, monitoringRace.OfficialStartAt);
        Assert.IsTrue(await store.HasActiveTaskAsync(race, definition));
        var resumed = await store.AcquireAsync(receipt.TaskId!.Value, 2, start, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(resumed);
        Assert.AreEqual(RaceArtifactStatus.Current.ToString(), resumed.Attributes["cardArtifactStatus"]);
        Assert.AreEqual(start, DateTimeOffset.Parse(resumed.Attributes["officialStartAt"]));
    }

    [TestMethod]
    public async Task RaceLocationOutcome_PersistsArtifactClassification()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        var race = new ResourceKey(ResourceType.Race, "JRA", "20260919:Tokyo:10");
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 2, "facets", false);
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(race, definition, 2, CollectionReason.Initial, now);
        var locationId = await store.UpsertLocationAsync(race, definition,
            new Uri("https://example.test/card/10"), ResourceLocationSource.Discovered, DateTimeOffset.UtcNow);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));

        await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [new(locationId, CollectionAttemptResult.Succeeded, Artifact: RaceArtifactKind.Card)]));

        var locations = await store.ResolveLocationsAsync(race, definition);
        Assert.AreEqual(RaceArtifactKind.Card, locations.Single().Artifact);
    }

    [TestMethod]
    public async Task RaceDetail_CardRepair_PreservesPersistedCurrentResult()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260919:Tokyo:1");
        var date = new DateOnly(2026, 9, 19);
        var now = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 2, "facets", false);
        var initial = await store.RequestAsync(resource, definition, 2, CollectionReason.Initial, now,
            effectiveDate: date);
        var initialLease = await store.AcquireAsync(initial.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(initial.TaskId!.Value, initialLease!.LeaseToken,
            now.AddSeconds(1), new(CollectionAttemptResult.Succeeded, StageOutcomes:
            [new("PersistResult", RaceArtifactKind.Result, CollectionAttemptResult.Succeeded, Persisted: true)])));
        var before = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!
            .Single(x => x.Artifact == RaceArtifactKind.Result);
        var cardUrl = new Uri("https://example.test/card/1");
        var refresh = await store.RequestAsync(resource, definition, 2, CollectionReason.ManualRefresh,
            now.AddSeconds(2), explicitUrl: cardUrl);
        var lease = await store.AcquireAsync(refresh.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));
        Assert.AreEqual("Current", lease!.Attributes["resultArtifactStatus"]);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => new JraRaceCardPage(url.AbsoluteUri,
                    new RaceId(date, RaceCourse.Tokyo, 1), "test", new(10, 0), []),
            },
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(now));
        var completion = await handler.CollectAsync(lease, CancellationToken.None);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsTrue(await store.CompleteAttemptAsync(refresh.TaskId!.Value, lease.LeaseToken,
            now.AddSeconds(3), completion));
        var after = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!
            .Single(x => x.Artifact == RaceArtifactKind.Result);
        Assert.AreEqual(RaceArtifactStatus.Current, after.Status);
        Assert.AreEqual(before.LastPersistedAt, after.LastPersistedAt);
        Assert.AreEqual(before.AppliedRevision, after.AppliedRevision);
        Assert.IsEmpty(results.Requests);
        Assert.IsEmpty(results.RefreshPageRequests);
    }

    [TestMethod]
    public async Task RaceDetail_NewerRevision_DoesNotLeaseStaleFacetsAsCurrent()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260919:Tokyo:1");
        var now = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 1, "first", false);
        var initial = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial, now,
            effectiveDate: new DateOnly(2026, 9, 19));
        var initialLease = await store.AcquireAsync(initial.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(initial.TaskId!.Value, initialLease!.LeaseToken,
            now.AddSeconds(1), new(CollectionAttemptResult.Succeeded, StageOutcomes:
            [
                new("PersistCard", RaceArtifactKind.Card, CollectionAttemptResult.Succeeded, Persisted: true),
                new("PersistResult", RaceArtifactKind.Result, CollectionAttemptResult.Succeeded, Persisted: true),
            ])));
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 2, "new parser", false);
        var refresh = await store.RequestAsync(resource, definition, 2, CollectionReason.DefinitionChanged,
            now.AddSeconds(2));
        var lease = await store.AcquireAsync(refresh.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.AreEqual("Due", lease.Attributes["cardArtifactStatus"]);
        Assert.AreEqual("Due", lease.Attributes["resultArtifactStatus"]);
        var facets = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!;
        Assert.IsTrue(facets.All(x => x.AppliedRevision == 1));
        var cards = new FakeJraRaceCardCollectionWorkflow();
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = race => new(race, "created-race", [1], [], "https://example.test/result", true),
        };
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(),
            _ => cards, _ => results, timeProvider: new FixedTimeProvider(now));
        var completion = await handler.CollectAsync(lease, CancellationToken.None);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.HasCount(1, cards.RefreshRequests);
        Assert.HasCount(1, results.Requests);
        Assert.IsTrue(await store.CompleteAttemptAsync(refresh.TaskId!.Value, lease.LeaseToken,
            now.AddSeconds(3), completion));
        var upgraded = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!;
        Assert.IsTrue(upgraded.All(x => x.Status == RaceArtifactStatus.Current && x.AppliedRevision == 2));
    }

    [TestMethod]
    [DataRow(true, 2)]
    [DataRow(true, 1)]
    [DataRow(false, 2)]
    public async Task RaceDetail_HistoricalCardUpgrade_ExposesUnavailableFacetWithoutBlockingResult(bool hasCard, int revision)
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260901:Tokyo:1");
        var now = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 1, "first", false);
        var initial = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial, now,
            effectiveDate: new DateOnly(2026, 9, 1));
        var initialLease = await store.AcquireAsync(initial.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        var stages = new List<CollectionStageOutcome>
        {
            new("PersistResult", RaceArtifactKind.Result, CollectionAttemptResult.Succeeded, Persisted: true),
        };
        if (hasCard)
            stages.Add(new("PersistCard", RaceArtifactKind.Card, CollectionAttemptResult.Succeeded, Persisted: true));
        Assert.IsTrue(await store.CompleteAttemptAsync(initial.TaskId!.Value, initialLease!.LeaseToken,
            now.AddSeconds(1), new(CollectionAttemptResult.Succeeded, StageOutcomes: stages)));
        if (revision > 1)
            await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, revision, "new parser", false);
        var refresh = await store.RequestAsync(resource, definition, revision, CollectionReason.ManualRefresh,
            now.AddSeconds(2));
        var lease = await store.AcquireAsync(refresh.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));
        var cards = new FakeJraRaceCardCollectionWorkflow();
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = race => new(race, "created-race", [1], [], "https://example.test/result", true),
        };
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => new JraRaceResultPage(
                    url.AbsoluteUri, new(new DateOnly(2026, 9, 1), RaceCourse.Tokyo, 1), "test", []),
            },
        };
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => cards, _ => results, timeProvider: new FixedTimeProvider(now));
        var completion = await handler.CollectAsync(lease!, CancellationToken.None);
        var unavailableUpgrade = hasCard && revision > 1;
        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsEmpty(cards.RefreshRequests);
        Assert.IsTrue(await store.CompleteAttemptAsync(refresh.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(3), completion));
        var detail = (await store.GetResourceDetailAsync(resource, definition))!;
        Assert.AreEqual(revision, detail.State!.AppliedRevision);
        var card = detail.RaceArtifacts!.SingleOrDefault(x => x.Artifact == RaceArtifactKind.Card);
        if (hasCard)
        {
            Assert.AreEqual(1, card!.AppliedRevision);
            Assert.AreEqual(unavailableUpgrade ? RaceArtifactStatus.Unavailable : RaceArtifactStatus.Current, card.Status);
            Assert.AreEqual(now.AddSeconds(1), card.LastPersistedAt);
            Assert.AreEqual(revision, card.RequiredRevision);
            if (unavailableUpgrade)
                Assert.AreEqual("OfficialRaceCardOutsideLookupPeriod", card.ErrorCode);
        }
        Assert.AreEqual(revision, detail.RaceArtifacts!.Single(x => x.Artifact == RaceArtifactKind.Result).AppliedRevision);
        Assert.IsFalse((await store.GetPipelineStateAsync()).IsPaused);
        if (unavailableUpgrade)
        {
            await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 3, "third parser", false);
            var next = await store.RequestAsync(resource, definition, 3, CollectionReason.DefinitionChanged, now.AddSeconds(4));
            var nextLease = await store.AcquireAsync(next.TaskId!.Value, 1, now.AddSeconds(4), TimeSpan.FromMinutes(5));
            var nextCompletion = await handler.CollectAsync(nextLease!, CancellationToken.None);
            Assert.AreEqual(CollectionAttemptResult.Succeeded, nextCompletion.Result, System.Text.Json.JsonSerializer.Serialize(nextCompletion));
            Assert.IsTrue(await store.CompleteAttemptAsync(next.TaskId!.Value, nextLease!.LeaseToken, now.AddSeconds(5), nextCompletion));
            var nextCard = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!
                .Single(x => x.Artifact == RaceArtifactKind.Card);
            Assert.AreEqual(3, nextCard.RequiredRevision);
            Assert.AreEqual(1, nextCard.AppliedRevision);
            Assert.AreEqual(now.AddSeconds(1), nextCard.LastPersistedAt);
            Assert.AreEqual(RaceArtifactStatus.Unavailable, nextCard.Status);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LegacyRaceMigration_PreservesCardEvidenceForLaterRevision(bool existingFacet)
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-detail");
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260901:Tokyo:1");
        var date = new DateOnly(2026, 9, 1);
        var now = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        await store.RegisterDefinitionAsync(new("race-card"), "Card", ResourceType.RaceCard, 7, "legacy", false);
        await store.RegisterDefinitionAsync(new("race-result"), "Result", ResourceType.RaceResult, 7, "legacy", false);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 1, "unified", false);
        var attributes = new Dictionary<string, string> { ["course"] = "東京", ["number"] = "1" };
        await store.InitializeFromDomainDataAsync([
            new(new(ResourceType.RaceCard, "JRA", "legacy-card"), new("race-card"), 7, now.AddDays(-10), date, attributes),
            new(new(ResourceType.RaceResult, "JRA", "legacy-result"), new("race-result"), 7, now.AddDays(-10), date, attributes),
        ], dryRun: false);
        if (existingFacet)
        {
            var initial = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial, now.AddMinutes(-2), effectiveDate: date);
            var initialLease = await store.AcquireAsync(initial.TaskId!.Value, 1, now.AddMinutes(-2), TimeSpan.FromMinutes(5));
            Assert.IsTrue(await store.CompleteAttemptAsync(initial.TaskId!.Value, initialLease!.LeaseToken, now.AddMinutes(-1),
                new(CollectionAttemptResult.Succeeded, StageOutcomes:
                [new("PersistCard", RaceArtifactKind.Card, CollectionAttemptResult.Succeeded, Persisted: true)])));
        }
        Assert.IsTrue((await store.MergeLegacyRaceDetailsAsync(false, now)).DryRun);
        Assert.IsEmpty((await store.MergeLegacyRaceDetailsAsync(true, now)).Errors);
        var migrated = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!;
        var card = migrated.Single(x => x.Artifact == RaceArtifactKind.Card);
        Assert.AreEqual(1, card.AppliedRevision, "Legacy extractor revision 7 maps to unified revision 1.");
        Assert.AreEqual(existingFacet ? now.AddMinutes(-1) : now.AddDays(-10), card.LastPersistedAt);
        Assert.AreEqual(0, (await store.MergeLegacyRaceDetailsAsync(true, now.AddSeconds(1))).SourceResources);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 2, "upgrade", false);
        var refresh = await store.RequestAsync(resource, definition, 2, CollectionReason.DefinitionChanged, now.AddSeconds(2));
        var lease = await store.AcquireAsync(refresh.TaskId!.Value, 1, now.AddSeconds(2), TimeSpan.FromMinutes(5));
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = race => new(race, "created-race", [1], [], "https://example.test/result", true),
        };
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(),
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results, timeProvider: new FixedTimeProvider(now));
        var completion = await handler.CollectAsync(lease!, CancellationToken.None);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsTrue(await store.CompleteAttemptAsync(refresh.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(3), completion));
        var after = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!;
        Assert.AreEqual(RaceArtifactStatus.Unavailable, after.Single(x => x.Artifact == RaceArtifactKind.Card).Status);
        Assert.AreEqual(card.LastPersistedAt, after.Single(x => x.Artifact == RaceArtifactKind.Card).LastPersistedAt);
        Assert.AreEqual(2, after.Single(x => x.Artifact == RaceArtifactKind.Result).AppliedRevision);
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task LegacyRaceMigration_PreservesUnifiedRevisionAndPreview(bool unifiedComplete, bool legacyComplete)
    {
        var store = CreateStore();
        var now = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 9, 1);
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260901:Tokyo:1");
        var definition = new CollectionDefinitionId("race-detail");
        await store.RegisterDefinitionAsync(new("race-result"), "Result", ResourceType.RaceResult, 7, "legacy", false);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 2, "unified", false);
        await store.InitializeFromDomainDataAsync([
            new(new(ResourceType.RaceResult, "JRA", "legacy-result"), new("race-result"), 7, now.AddDays(-10), date,
                new Dictionary<string, string> { ["course"] = "Tokyo", ["number"] = "1" }, IsComplete: legacyComplete),
            new(resource, definition, 2, now.AddDays(-1), date, new Dictionary<string, string>(), IsComplete: unifiedComplete),
        ], dryRun: false);
        var before = (await store.GetStateAsync(resource, definition))!;
        var preview = await store.MergeLegacyRaceDetailsAsync(false, now);
        var applied = await store.MergeLegacyRaceDetailsAsync(true, now);
        Assert.IsEmpty(applied.Errors);
        Assert.AreEqual(unifiedComplete ? 0 : 1, preview.SupplementRequests);
        Assert.AreEqual(preview.SupplementRequests, applied.SupplementRequests);
        var after = (await store.GetStateAsync(resource, definition))!;
        Assert.AreEqual(before.RequiredRevision, after.RequiredRevision);
        Assert.AreEqual(before.AppliedRevision, after.AppliedRevision);
        Assert.AreEqual(before.LastCollectedAt, after.LastCollectedAt);
        Assert.AreEqual(unifiedComplete ? CollectionStateStatus.Current : CollectionStateStatus.Pending, after.Status);
        if (!unifiedComplete)
            Assert.AreEqual(2, (await store.GetTasksAsync()).Single().RequestedRevision);
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public async Task LegacyRaceMigration_OnlyImportsKnownPersistenceEvidence(bool knownTimestamp, bool existingFacet)
    {
        var store = CreateStore();
        var now = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 9, 1);
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260901:Tokyo:1");
        var definition = new CollectionDefinitionId("race-detail");
        await store.RegisterDefinitionAsync(new("race-card"), "Card", ResourceType.RaceCard, 7, "legacy", false);
        await store.RegisterDefinitionAsync(definition, "Race detail", ResourceType.Race, 2, "unified", false);
        await store.InitializeFromDomainDataAsync([
            new(new(ResourceType.RaceCard, "JRA", "legacy-card"), new("race-card"), 7, now.AddDays(-10), date,
                new Dictionary<string, string> { ["course"] = "Tokyo", ["number"] = "1" }),
            new(resource, definition, 2, now.AddDays(-1), date, new Dictionary<string, string>()),
        ], dryRun: false);
        await using (var db = new CollectionPlatformDbContext(new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False").Options))
        {
            var target = await db.Resources.SingleAsync(x => x.Type == ResourceType.Race);
            if (existingFacet)
                db.RaceArtifactStates.Add(new RaceArtifactStateEntity
                {
                    ResourcePk = target.ResourcePk,
                    Artifact = RaceArtifactKind.Card,
                    Status = RaceArtifactStatus.Blocked,
                    AppliedRevision = 0,
                    RequiredRevision = 2,
                    ErrorCode = "KeepError",
                    UpdatedAt = now.AddDays(-1),
                });
            if (!knownTimestamp)
                (await db.States.SingleAsync(x => x.DefinitionId == "race-card")).LastCollectedAt = null;
            await db.SaveChangesAsync();
        }
        Assert.IsEmpty((await store.MergeLegacyRaceDetailsAsync(true, now)).Errors);
        var artifacts = (await store.GetResourceDetailAsync(resource, definition))!.RaceArtifacts!;
        if (!existingFacet)
        {
            Assert.IsEmpty(artifacts, "A legacy Current flag without a persistence timestamp is not proof of collection.");
            return;
        }
        var card = artifacts.Single();
        Assert.AreEqual(knownTimestamp ? 1 : 0, card.AppliedRevision);
        Assert.AreEqual(knownTimestamp ? now.AddDays(-10) : (DateTimeOffset?)null, card.LastPersistedAt);
        Assert.AreEqual(2, card.RequiredRevision);
        Assert.AreEqual(RaceArtifactStatus.Blocked, card.Status);
        Assert.AreEqual("KeepError", card.ErrorCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<CollectionPlatformStore> CreateStoreAsync()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(HorseProfile, "Horse profile", ResourceType.Horse, 7,
            "Initial profile extractor", false);
        return store;
    }

    [TestMethod]
    public async Task LegacyRaceMigration_MergesResourcesAndQueuesMissingRecentResult()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(new("race-card"), "Race card", ResourceType.RaceCard, 1, "initial", false);
        await store.RegisterDefinitionAsync(new("race-result"), "Race result", ResourceType.RaceResult, 1, "initial", false);
        await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", ResourceType.Race, 1, "initial", false);
        var now = new DateTimeOffset(2026, 9, 14, 1, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 9, 13);
        var attributes = new Dictionary<string, string> { ["course"] = "中山", ["number"] = "11" };
        await store.InitializeFromDomainDataAsync([
            new(new(ResourceType.RaceCard, "JRA", "legacy-card"), new("race-card"), 1, now.AddDays(-1),
                date, attributes),
            new(new(ResourceType.RaceResult, "JRA", "legacy-result"), new("race-result"), 1, now.AddDays(-1),
                date, attributes, IsComplete: false),
        ], dryRun: false);

        var preview = await store.MergeLegacyRaceDetailsAsync(false, now);
        Assert.IsTrue(preview.DryRun);
        Assert.AreEqual(2, preview.SourceResources);
        Assert.AreEqual(1, preview.TargetResources);
        Assert.AreEqual(1, preview.SupplementRequests);

        var applied = await store.MergeLegacyRaceDetailsAsync(true, now);
        Assert.IsEmpty(applied.Errors);
        Assert.AreEqual(1, applied.SupplementRequests);
        var target = new ResourceKey(ResourceType.Race, "JRA", "20260913:Nakayama:11");
        var state = await store.GetStateAsync(target, new("race-detail"));
        Assert.IsNotNull(state);
        Assert.AreEqual(CollectionStateStatus.Pending, state.Status);
        Assert.HasCount(1, await store.GetTasksAsync());
        Assert.IsNull(await store.GetStateAsync(new(ResourceType.RaceCard, "JRA", "legacy-card"), new("race-card")));

        var repeated = await store.MergeLegacyRaceDetailsAsync(true, now.AddMinutes(1));
        Assert.AreEqual(0, repeated.SourceResources);
        Assert.AreEqual(0, repeated.SupplementRequests);
    }

    [TestMethod]
    public async Task LegacyRaceMigration_CancelsActiveLegacyTaskAndQueuesUnifiedReplacement()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(new("race-card"), "Race card", ResourceType.RaceCard, 1, "initial", false);
        await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", ResourceType.Race, 1, "initial", false);
        var now = new DateTimeOffset(2026, 9, 14, 1, 0, 0, TimeSpan.Zero);
        var attributes = new Dictionary<string, string> { ["course"] = "中山", ["number"] = "11" };
        var legacy = await store.RequestAsync(new(ResourceType.RaceCard, "JRA", "legacy-card"),
            new("race-card"), 1, CollectionReason.Initial, now, effectiveDate: new(2026, 9, 13),
            attributes: attributes);

        var applied = await store.MergeLegacyRaceDetailsAsync(true, now.AddMinutes(1));

        Assert.IsEmpty(applied.Errors);
        Assert.AreEqual(1, applied.SupplementRequests);
        var tasks = await store.GetTasksAsync();
        Assert.AreEqual(CollectionTaskStatus.Cancelled, tasks.Single(x => x.TaskId == legacy.TaskId!.Value).Status);
        Assert.IsTrue(tasks.Any(x => x.TaskId != legacy.TaskId!.Value && x.Definition.Value == "race-detail"
            && x.Status == CollectionTaskStatus.Ready));
    }

    [TestMethod]
    public async Task Readiness_CountsOnlyActiveRequestsForRequestedRace()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", ResourceType.Horse, 1, "Initial", false);
        await store.RegisterDefinitionAsync(new("jockey-profile"), "Jockey", ResourceType.Jockey, 1, "Initial", false);
        await store.RegisterDefinitionAsync(new("trainer-profile"), "Trainer", ResourceType.Trainer, 1, "Initial", false);
        var now = DateTimeOffset.UtcNow;
        var attributes = new Dictionary<string, string> { ["requestedByRaceId"] = "race-1" };
        var horse = await store.RequestAsync(new(ResourceType.Horse, "JRA", "h1"), new("horse-profile"),
            1, CollectionReason.Discovery, now, attributes: attributes);
        await store.RequestAsync(new(ResourceType.Jockey, "JRA", "j1"), new("jockey-profile"),
            1, CollectionReason.Discovery, now, attributes: attributes);
        await store.RequestAsync(new(ResourceType.Trainer, "JRA", "t2"), new("trainer-profile"),
            1, CollectionReason.Discovery, now, attributes: new Dictionary<string, string> { ["requestedByRaceId"] = "race-2" });

        var before = await store.GetReadinessAsync("race-1");
        Assert.AreEqual(1, before.PendingHorseRequests);
        Assert.AreEqual(1, before.PendingJockeyRequests);
        Assert.AreEqual(0, before.PendingTrainerRequests);
        Assert.AreEqual(2, before.TotalPendingRequests);

        await CompleteAsync(store, horse, now);
        var after = await store.GetReadinessAsync("race-1");
        Assert.AreEqual(0, after.PendingHorseRequests);
        Assert.AreEqual(1, after.TotalPendingRequests);
    }

    [TestMethod]
    public async Task ResourceDetail_HistoriesArePagedBeforeReturning()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("horse-profile");
        var resource = new ResourceKey(ResourceType.Horse, "JRA", "history-horse");
        await store.RegisterDefinitionAsync(definition, "Horse", ResourceType.Horse, 1, "Initial", false);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        for (var index = 0; index < 30; index++)
            await store.RequestAsync(resource, definition, 1, CollectionReason.ManualRefresh, now.AddSeconds(index));

        var secondPage = await store.GetResourceDetailPagedAsync(resource, definition, 2, 1, 1, 25);

        Assert.IsNotNull(secondPage);
        Assert.AreEqual(30, secondPage.RequestTotal);
        Assert.HasCount(5, secondPage.Requests);
        Assert.AreEqual(2, secondPage.HistoryPage);
        Assert.AreEqual(25, secondPage.HistoryPageSize);
        Assert.IsNotNull(secondPage.LatestTask);
        Assert.AreEqual(1, secondPage.EffectiveTaskHistoryPage);
        Assert.AreEqual(1, secondPage.EffectiveAttemptHistoryPage);
    }

    [TestMethod]
    public async Task ResourceDetail_PagesEachHistoryIndependently_AndAlwaysReturnsLatestTask()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("horse-profile");
        var resource = new ResourceKey(ResourceType.Horse, "JRA", "independent-history-horse");
        await store.RegisterDefinitionAsync(definition, "Horse", ResourceType.Horse, 1, "Initial", false);
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var receipts = new List<CollectionRequestReceipt>();
        for (var index = 0; index < 3; index++)
        {
            var requestedAt = now.AddMinutes(index * 2);
            var receipt = await store.RequestAsync(resource, definition, 1,
                CollectionReason.ManualRefresh, requestedAt);
            receipts.Add(receipt);
            await CompleteAsync(store, receipt, requestedAt);
        }

        var detail = await store.GetResourceDetailPagedAsync(resource, definition,
            requestHistoryPage: 2, taskHistoryPage: 3, attemptHistoryPage: 2, historyPageSize: 1);

        Assert.IsNotNull(detail);
        Assert.AreEqual(3, detail.RequestTotal);
        Assert.AreEqual(3, detail.TaskTotal);
        Assert.AreEqual(3, detail.AttemptTotal);
        Assert.HasCount(1, detail.Requests);
        Assert.HasCount(1, detail.Tasks);
        Assert.HasCount(1, detail.Attempts);
        Assert.AreEqual(receipts[0].TaskId, detail.Tasks[0].TaskId);
        Assert.AreEqual(receipts[1].TaskId, detail.Attempts[0].TaskId);
        Assert.AreEqual(receipts[2].TaskId, detail.LatestTask?.TaskId);
        Assert.AreEqual(2, detail.RequestHistoryPage);
        Assert.AreEqual(3, detail.EffectiveTaskHistoryPage);
        Assert.AreEqual(2, detail.EffectiveAttemptHistoryPage);
    }

    [TestMethod]
    public async Task ResourceDetail_NormalizesInvalidPaging_AndExtremePageReturnsEmpty()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now);
        await CompleteAsync(store, receipt, now);

        var normalized = await store.GetResourceDetailPagedAsync(Horse, HorseProfile,
            requestHistoryPage: 0, taskHistoryPage: -4, attemptHistoryPage: 0, historyPageSize: 0);
        var extreme = await store.GetResourceDetailPagedAsync(Horse, HorseProfile,
            requestHistoryPage: int.MaxValue, taskHistoryPage: int.MaxValue,
            attemptHistoryPage: int.MaxValue, historyPageSize: int.MaxValue);

        Assert.IsNotNull(normalized);
        Assert.AreEqual(1, normalized.RequestHistoryPage);
        Assert.AreEqual(1, normalized.EffectiveTaskHistoryPage);
        Assert.AreEqual(1, normalized.EffectiveAttemptHistoryPage);
        Assert.AreEqual(1, normalized.HistoryPageSize);
        Assert.HasCount(1, normalized.Requests);
        Assert.IsNotNull(extreme);
        Assert.AreEqual(100, extreme.HistoryPageSize);
        Assert.IsEmpty(extreme.Requests);
        Assert.IsEmpty(extreme.Tasks);
        Assert.IsEmpty(extreme.Attempts);
        Assert.AreEqual(receipt.TaskId!.Value, extreme.LatestTask?.TaskId);
    }

    [TestMethod]
    public async Task ResourceDetail_SameTimestampOrderingIsStableAcrossPages()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 5; index++)
            await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now);

        var first = await store.GetResourceDetailPagedAsync(Horse, HorseProfile, 1, 1, 1, 2);
        var second = await store.GetResourceDetailPagedAsync(Horse, HorseProfile, 2, 2, 2, 2);
        var repeated = await store.GetResourceDetailPagedAsync(Horse, HorseProfile, 1, 1, 1, 2);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsNotNull(repeated);
        CollectionAssert.AreEqual(first.Requests.Select(x => x.RequestId).ToArray(),
            repeated.Requests.Select(x => x.RequestId).ToArray());
        Assert.IsEmpty(first.Requests.Select(x => x.RequestId).Intersect(second.Requests.Select(x => x.RequestId)));
        Assert.AreEqual(first.LatestTask?.TaskId, second.LatestTask?.TaskId);
    }

    private CollectionPlatformStore CreateStore() => new(Options.Create(new CollectionPlatformOptions
    {
        StateDirectory = _directory,
    }));

    private static async Task CompleteAsync(CollectionPlatformStore store, CollectionRequestReceipt receipt,
        DateTimeOffset now)
    {
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded)));
    }

    private sealed class LegacyLayoutCondition : INamedRevisionImpactCondition
    {
        public string Name => "horse-profile:legacy-layout";
        public bool Matches(RevisionResourceCandidate candidate)
            => candidate.Attributes.GetValueOrDefault("layout") == "legacy";
    }
    [TestMethod]
    public async Task FailureResolution_IsIndependentFromPublishing_AndTracksRecoveryLifecycle()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var failed = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var failedLease = await store.AcquireAsync(failed.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failed.TaskId!.Value, failedLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "Broken"));
        await store.SetPausedAsync(false, null, now.AddSeconds(2));
        var open = (await store.GetActionableFailureNotificationsAsync(now.AddMinutes(1), 10)).Single();

        await store.MarkFailureNotificationPublishedAsync(open.NotificationId, now.AddMinutes(1));
        Assert.IsEmpty(await store.GetUnpublishedFailureNotificationsAsync(now.AddMinutes(2), 10));
        Assert.HasCount(1, await store.GetActionableFailureNotificationsAsync(now.AddMinutes(2), 10));
        Assert.AreEqual(1, (await store.SearchTasksAsync(new(
            Statuses: [CollectionTaskStatus.Failed, CollectionTaskStatus.DeadLetter], ActionableOnly: true))).TotalCount);
        Assert.AreEqual(1, (await store.GetTaskViewCountsAsync()).Counts["attention"]);

        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh,
            now.AddMinutes(2));
        Assert.IsEmpty(await store.GetActionableFailureNotificationsAsync(now.AddMinutes(2), 10));
        Assert.AreEqual(0, (await store.SearchTasksAsync(new(
            Statuses: [CollectionTaskStatus.Failed, CollectionTaskStatus.DeadLetter], ActionableOnly: true))).TotalCount);
        Assert.AreEqual(1, (await store.SearchTasksAsync(new(
            Statuses: [CollectionTaskStatus.Failed, CollectionTaskStatus.DeadLetter]))).TotalCount,
            "The original failed task remains available as history.");
        Assert.AreEqual(0, (await store.GetTaskViewCountsAsync()).Counts["attention"]);
        var during = await store.GetResourceDetailAsync(Horse, HorseProfile);
        var duringFailure = during!.Failures!.Single();
        Assert.AreEqual(CollectionFailureResolutionStatus.RecoveryInProgress,
            duringFailure.ResolutionStatus);
        Assert.AreEqual(recovery.TaskId!.Value, duringFailure.RecoveryTaskId);

        var lease = await store.AcquireAsync(recovery.TaskId!.Value, 1, now.AddMinutes(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        await store.CompleteAttemptAsync(recovery.TaskId!.Value, lease.LeaseToken, now.AddMinutes(3),
            new(CollectionAttemptResult.Succeeded));
        var resolved = await store.GetResourceDetailAsync(Horse, HorseProfile);
        var resolvedFailure = resolved!.Failures!.Single();
        Assert.AreEqual(CollectionFailureResolutionStatus.Resolved, resolvedFailure.ResolutionStatus);
        Assert.IsNotNull(resolvedFailure.ResolvedAt);
    }

    [TestMethod]
    public async Task DismissFailureNotifications_PreservesHistory_IsIdempotent_AndAllowsFutureFailure()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var failed = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var failedLease = await store.AcquireAsync(failed.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(failed.TaskId!.Value, failedLease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "OldFailure",
                FailureImpact: CollectionFailureImpact.Isolated));
        var notification = (await store.GetActionableFailureNotificationsAsync(now.AddMinutes(1), 10)).Single();

        var first = await store.DismissFailureNotificationsAsync([notification.NotificationId], now.AddMinutes(2));
        var repeated = await store.DismissFailureNotificationsAsync([notification.NotificationId], now.AddMinutes(3));

        Assert.AreEqual(1, first.DismissedCount);
        Assert.AreEqual(0, first.AlreadyClosedCount);
        Assert.AreEqual(0, repeated.DismissedCount);
        Assert.AreEqual(1, repeated.AlreadyClosedCount);
        Assert.IsEmpty(await store.GetActionableFailureNotificationsAsync(now.AddMinutes(3), 10));
        Assert.AreEqual(CollectionTaskStatus.Failed,
            (await store.GetTasksAsync()).Single(x => x.TaskId == failed.TaskId!.Value).Status);
        Assert.AreEqual(CollectionStateStatus.Failed, (await store.GetStateAsync(Horse, HorseProfile))!.Status);
        var oldFailure = (await store.GetResourceDetailAsync(Horse, HorseProfile))!.Failures!.Single();
        Assert.AreEqual(CollectionFailureResolutionStatus.Superseded, oldFailure.ResolutionStatus);
        Assert.IsNotNull(oldFailure.ResolvedAt);

        var next = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh,
            now.AddMinutes(4));
        var nextLease = await store.AcquireAsync(next.TaskId!.Value, 1, now.AddMinutes(4), TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(next.TaskId!.Value, nextLease!.LeaseToken, now.AddMinutes(5),
            new(CollectionAttemptResult.PermanentFailure, "NewFailure",
                FailureImpact: CollectionFailureImpact.Isolated));
        var newNotification = (await store.GetActionableFailureNotificationsAsync(now.AddMinutes(6), 10)).Single();
        Assert.AreEqual("NewFailure", newNotification.ErrorCode);
        Assert.AreNotEqual(notification.NotificationId, newNotification.NotificationId);
    }

    [TestMethod]
    public async Task DismissFailureNotifications_RecoveryInProgressDoesNotPartiallyUpdate()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var firstResource = Horse;
        var secondResource = new ResourceKey(ResourceType.Horse, "JRA", "H456");
        var ids = new List<Guid>();
        foreach (var resource in new[] { firstResource, secondResource })
        {
            var failed = await store.RequestAsync(resource, HorseProfile, 7, CollectionReason.Initial, now);
            var lease = await store.AcquireAsync(failed.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
            await store.CompleteAttemptAsync(failed.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.PermanentFailure, "Broken",
                    FailureImpact: CollectionFailureImpact.Isolated));
        }
        ids.AddRange((await store.GetActionableFailureNotificationsAsync(now.AddMinutes(1), 10))
            .Select(x => x.NotificationId));
        await store.RequestAsync(firstResource, HorseProfile, 7, CollectionReason.Recovery, now.AddMinutes(2));

        var result = await store.DismissFailureNotificationsAsync(ids, now.AddMinutes(3));

        Assert.IsTrue(result.HasRecoveryConflict);
        Assert.AreEqual(0, result.DismissedCount);
        var details = await store.GetFailureNotificationsAsync(ids);
        Assert.IsTrue(details.Any(x => x.ResolutionStatus == CollectionFailureResolutionStatus.RecoveryInProgress));
        Assert.IsTrue(details.Any(x => x.ResolutionStatus == CollectionFailureResolutionStatus.Open));
    }

    [TestMethod]
    public async Task FailedRecovery_SupersedesOldFailure_AndLeavesOnlyLatestActionable()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var firstLease = await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(first.TaskId!.Value, firstLease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "Old"));
        await store.SetPausedAsync(false, null, now.AddSeconds(2));
        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery, now.AddMinutes(1));
        var recoveryLease = await store.AcquireAsync(recovery.TaskId!.Value, 1, now.AddMinutes(1), TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(recovery.TaskId!.Value, recoveryLease!.LeaseToken, now.AddMinutes(2),
            new(CollectionAttemptResult.PermanentFailure, "New"));

        var actionable = (await store.GetActionableFailureNotificationsAsync(now.AddMinutes(3), 10)).Single();
        Assert.AreEqual("New", actionable.ErrorCode);
        var history = (await store.GetResourceDetailAsync(Horse, HorseProfile))!.Failures!;
        Assert.HasCount(2, history);
        Assert.AreEqual(CollectionFailureResolutionStatus.Superseded,
            history.Single(x => x.ErrorCode == "Old").ResolutionStatus);
    }

    [TestMethod]
    public async Task CancelledRecovery_ReopensFailureAndRestoresFailedState()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var failed = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(failed.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(failed.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "Broken"));
        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery, now.AddMinutes(1));

        Assert.IsTrue(await store.CancelTaskAsync(recovery.TaskId!.Value, now.AddMinutes(2)));

        var actionable = (await store.GetActionableFailureNotificationsAsync(now.AddMinutes(3), 10)).Single();
        Assert.AreEqual(CollectionFailureResolutionStatus.Open, actionable.ResolutionStatus);
        Assert.IsNull(actionable.RecoveryTaskId);
        Assert.AreEqual(CollectionStateStatus.Failed, (await store.GetStateAsync(Horse, HorseProfile))!.Status);
    }

    private sealed class TransactionCounterInterceptor : DbTransactionInterceptor
    {
        public int Commits { get; set; }
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
            Commits++;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Commits++;
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task SuppressResource_CancelsPendingTasksAndRejectsFutureRequests()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);

        var result = await store.SuppressResourceAsync(Horse, "Merged horse was deleted", "repair-1",
            now.AddMinutes(1));

        Assert.AreEqual(1, result.CancelledTasks);
        Assert.AreEqual(CollectionTaskStatus.Cancelled,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId!.Value).Status);
        Assert.AreEqual(CollectionStateStatus.Unavailable, (await store.GetStateAsync(Horse, HorseProfile))!.Status);
        await Assert.ThrowsExactlyAsync<CollectionResourceSuppressedException>(() =>
            store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now.AddMinutes(2)));
        var canonical = new ResourceKey(ResourceType.Horse, "JRA", "H456");
        Assert.IsTrue((await store.RequestAsync(canonical, HorseProfile, 7, CollectionReason.Initial,
            now.AddMinutes(2))).CreatedTask);
    }

    [TestMethod]
    public async Task SuppressResource_RunningTaskIsCancellationRequestedAndOperationIsIdempotent()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        var first = await store.SuppressResourceAsync(Horse, "Merged horse was deleted", "repair-1",
            now.AddMinutes(1));
        var second = await store.SuppressResourceAsync(Horse, "Merged horse was deleted", "repair-1",
            now.AddMinutes(2));

        Assert.AreEqual(1, first.RunningCancellationRequests);
        Assert.AreEqual(0, second.RunningCancellationRequests);
        Assert.IsFalse(await store.HeartbeatAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddMinutes(2),
            TimeSpan.FromMinutes(5)));
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddMinutes(3),
            new(CollectionAttemptResult.Cancelled)));
        Assert.AreEqual(CollectionTaskStatus.Cancelled,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId!.Value).Status);
        Assert.AreEqual(CollectionStateStatus.Unavailable, (await store.GetStateAsync(Horse, HorseProfile))!.Status);
    }

    [TestMethod]
    public async Task IsolatedPermanentFailure_QueuesNotificationWithoutPausingPipeline()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "StructuralPageFailure", "missing heading",
                FailureImpact: CollectionFailureImpact.Isolated)));

        Assert.IsFalse((await store.GetPipelineStateAsync()).IsPaused);
        Assert.AreEqual(CollectionStateStatus.Failed, (await store.GetStateAsync(Horse, HorseProfile))!.Status);
        Assert.AreEqual("StructuralPageFailure",
            (await store.GetPendingFailureNotificationsAsync(now.AddSeconds(2), 10)).Single().ErrorCode);
    }

    [TestMethod]
    public async Task ClosedSessionFailures_RetryInIsolationUntilBurstThresholdThenPause()
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = _directory,
            ClosedSessionFailureThreshold = 3,
            ClosedSessionFailureWindowMinutes = 10,
        }));
        await store.RegisterDefinitionAsync(HorseProfile, "Horse profile", ResourceType.Horse, 7,
            "Initial profile extractor", false);
        var now = new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);

        for (var index = 1; index <= 3; index++)
        {
            var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", $"CLOSED-{index}"),
                HorseProfile, 7, CollectionReason.Initial, now.AddSeconds(index));
            var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now.AddSeconds(index),
                TimeSpan.FromMinutes(5));
            Assert.IsNotNull(lease);
            Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken,
                now.AddSeconds(index * 10),
                new(CollectionAttemptResult.TransientFailure, "TargetClosedException",
                    "Target page, context or browser has been closed")));

            var task = (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId!.Value);
            Assert.AreEqual(index < 3 ? CollectionTaskStatus.Ready : CollectionTaskStatus.Failed, task.Status);
            Assert.AreEqual(index >= 3, (await store.GetPipelineStateAsync()).IsPaused);
        }

        var notification = (await store.GetPendingFailureNotificationsAsync(now.AddMinutes(1), 10)).Single();
        Assert.AreEqual("TargetClosedException", notification.ErrorCode);
        StringAssert.Contains((await store.GetPipelineStateAsync()).Reason!, "TargetClosedException");
    }

    [TestMethod]
    public async Task SuppressResource_SupersedesActionableFailureNotifications()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        await store.CompleteAttemptAsync(receipt.TaskId!.Value, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "Deleted source horse"));
        Assert.HasCount(1, await store.GetActionableFailureNotificationsAsync(now.AddMinutes(1), 10));

        await store.SuppressResourceAsync(Horse, "Merged horse was deleted", "repair-1", now.AddMinutes(2));

        Assert.IsEmpty(await store.GetActionableFailureNotificationsAsync(now.AddMinutes(3), 10));
    }

    [TestMethod]
    public async Task SuppressResource_ExpiredRunningLeaseRemainsUnavailable()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        Assert.IsNotNull(await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(1)));
        await store.SuppressResourceAsync(Horse, "Merged horse was deleted", "repair-1", now.AddSeconds(10));

        Assert.AreEqual(1, await store.ReclaimExpiredLeasesAsync(now.AddMinutes(2)));

        Assert.AreEqual(CollectionTaskStatus.Cancelled,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId!.Value).Status);
        Assert.AreEqual(CollectionStateStatus.Unavailable, (await store.GetStateAsync(Horse, HorseProfile))!.Status);
    }

    [TestMethod]
    public async Task ExecutionLease_StartPendingIsIdempotentAndHoldsCapacityUntilExpiry()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "H456"), HorseProfile, 7,
            CollectionReason.Initial, now.AddMilliseconds(1));
        var pending = await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10);
        var firstEnvelope = Guid.NewGuid();
        var firstToken = Guid.NewGuid().ToString("N");
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending[0].OutboxId], firstToken,
            firstEnvelope, now, TimeSpan.FromSeconds(45), 1));
        var wake = new CollectionWakeSignal(Guid.NewGuid(), firstEnvelope, firstToken);
        var acquired = await store.AcquireNextExecutionAsync(wake, "message-1", now.AddSeconds(1),
            TimeSpan.FromSeconds(45));
        var repeated = await store.AcquireNextExecutionAsync(wake, "message-1", now.AddSeconds(2),
            TimeSpan.FromSeconds(45));
        var forged = await store.AcquireNextExecutionAsync(wake with { ReservationToken = "different" },
            "message-forged", now.AddSeconds(2), TimeSpan.FromSeconds(45));

        Assert.AreEqual(CollectionExecutionAcquireStatus.Acquired, acquired.Status);
        Assert.AreEqual(acquired.ExecutionBatchId, repeated.ExecutionBatchId);
        Assert.AreEqual(acquired.LeaseToken, repeated.LeaseToken);
        Assert.AreEqual(CollectionExecutionAcquireStatus.NoWork, forged.Status);
        Assert.IsFalse(await store.TryReserveDispatchesWithinCapacityAsync([pending[1].OutboxId],
            Guid.NewGuid().ToString("N"), Guid.NewGuid(), now.AddSeconds(3), TimeSpan.FromSeconds(45), 1));
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending[1].OutboxId],
            Guid.NewGuid().ToString("N"), Guid.NewGuid(), now.AddSeconds(47), TimeSpan.FromSeconds(45), 1));
    }

    [TestMethod]
    public async Task ExecutionLease_CompleteReturnsUnstartedTaskAndFreesSlot()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var pending = (await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10)).Single();
        var envelopeId = Guid.NewGuid();
        var reservation = Guid.NewGuid().ToString("N");
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([pending.OutboxId], reservation,
            envelopeId, now, TimeSpan.FromSeconds(45), 1));
        var acquired = await store.AcquireNextExecutionAsync(
            new(Guid.NewGuid(), envelopeId, reservation), "message-1", now.AddSeconds(1), TimeSpan.FromSeconds(45));
        Assert.IsTrue(await store.StartExecutionAsync(acquired.ExecutionBatchId!.Value,
            new(acquired.LeaseToken!, 960, "lambda-1"), now.AddSeconds(2)));
        Assert.IsTrue(await store.CompleteExecutionAsync(acquired.ExecutionBatchId.Value,
            acquired.LeaseToken!, now.AddSeconds(3)));
        Assert.IsTrue(await store.CompleteExecutionAsync(acquired.ExecutionBatchId.Value,
            acquired.LeaseToken!, now.AddSeconds(4)), "completion must be idempotent");
        Assert.HasCount(1, await store.GetPendingDispatchesAsync(now.AddSeconds(5), 10));
    }

    [TestMethod]
    public async Task WakeReservations_NeverExceedConfiguredExecutionSlots()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "SLOT-1", "SLOT-2", "SLOT-3" })
            await store.RequestAsync(new(ResourceType.Horse, "JRA", id), HorseProfile, 7,
                CollectionReason.Initial, now);
        var pending = await store.GetPendingDispatchesAsync(now.AddSeconds(1), 10);

        Assert.IsTrue(await Reserve(0));
        Assert.IsTrue(await Reserve(1));
        Assert.IsFalse(await Reserve(2));

        Task<bool> Reserve(int index) => store.TryReserveDispatchesWithinCapacityAsync(
            [pending[index].OutboxId], Guid.NewGuid().ToString("N"), Guid.NewGuid(), now,
            TimeSpan.FromSeconds(45), 2);
    }
}
