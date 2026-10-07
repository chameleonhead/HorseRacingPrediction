using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionRuntimeStatusRecorderTests
{
    private static readonly CollectionRuntimeAction[] Actions = Enum.GetValues<CollectionRuntimeAction>();

    [TestMethod]
    public void Snapshot_ContainsFixedActionSlotsAndSeparatesDisabledFromUnobserved()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(9)));
        var recorder = CreateRecorder(clock, disabled: CollectionRuntimeAction.Alerts);

        var response = recorder.GetSnapshot();
        var runtime = response.Runtime;

        Assert.AreNotEqual(Guid.Empty, runtime.InstanceId);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-08T03:00:00Z"), runtime.InstanceStartedAtUtc);
        Assert.AreEqual(runtime.InstanceStartedAtUtc, runtime.GeneratedAtUtc);
        CollectionAssert.AreEqual(Actions, runtime.Actions.Select(x => x.Action).ToArray());
        Assert.AreEqual(CollectionRuntimeState.NotObserved,
            runtime.Actions.Single(x => x.Action == CollectionRuntimeAction.Dispatcher).State);
        var disabled = runtime.Actions.Single(x => x.Action == CollectionRuntimeAction.Alerts);
        Assert.IsFalse(disabled.Enabled);
        Assert.AreEqual(CollectionRuntimeState.Disabled, disabled.State);
        Assert.AreEqual(TimeSpan.FromSeconds(1), disabled.EffectiveInterval);
        Assert.IsNull(runtime.Actions.Single(x => x.Action == CollectionRuntimeAction.MetricDelivery).EffectiveInterval);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CollectionRuntimeActionStatusDto>)runtime.Actions)[0] = runtime.Actions[1]);

        var restarted = CreateRecorder(clock).GetSnapshot().Runtime;
        Assert.AreNotEqual(runtime.InstanceId, restarted.InstanceId);
        Assert.IsTrue(restarted.Actions.All(x => x.LastStartedAtUtc is null
            && x.LastCompletedAtUtc is null && x.LastSuccessfulCycleAtUtc is null && x.LastProgressAtUtc is null));
    }

    [TestMethod]
    public void CompleteEmptyCycle_UpdatesSuccessAndDurationButNotProgress()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-08T03:00:00Z"));
        var recorder = CreateRecorder(clock);
        var token = recorder.BeginCycle(CollectionRuntimeAction.Dispatcher)!.Value;
        clock.Advance(TimeSpan.FromMilliseconds(1250));

        Assert.IsTrue(recorder.CompleteCycle(token, CollectionRuntimeReason.NoDueWork,
            new(Inspected: 4)));

        var action = GetAction(recorder, CollectionRuntimeAction.Dispatcher);
        Assert.AreEqual(CollectionRuntimeState.Waiting, action.State);
        Assert.AreEqual(CollectionRuntimeReason.NoDueWork, action.Reason);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-08T03:00:00Z"), action.LastStartedAtUtc);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-08T03:00:01.250Z"), action.LastCompletedAtUtc);
        Assert.AreEqual(action.LastCompletedAtUtc, action.LastSuccessfulCycleAtUtc);
        Assert.AreEqual(1250, action.LastDurationMilliseconds);
        Assert.AreEqual(4, action.InspectedCount);
        Assert.IsNull(action.LastProgressAtUtc);
        Assert.AreEqual(0, action.ConsecutiveErrors);
    }

    [TestMethod]
    public void Progress_IsExplicitAndStaleCycleCallbacksCannotOverwriteNewerCycle()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-08T03:00:00Z"));
        var recorder = CreateRecorder(clock);
        var oldToken = recorder.BeginCycle(CollectionRuntimeAction.BackfillRecovery)!.Value;
        clock.Advance(TimeSpan.FromSeconds(1));
        var currentToken = recorder.BeginCycle(CollectionRuntimeAction.BackfillRecovery)!.Value;
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.IsFalse(recorder.RecordProgress(oldToken));
        Assert.IsFalse(recorder.CompleteCycle(oldToken, CollectionRuntimeReason.NoDueWork,
            new(Completed: 99)));
        Assert.IsTrue(recorder.RecordProgress(currentToken));
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-08T03:00:03Z"),
            GetAction(recorder, CollectionRuntimeAction.BackfillRecovery).LastProgressAtUtc);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.IsTrue(recorder.CompleteCycle(currentToken, CollectionRuntimeReason.BudgetExhausted,
            new(Inspected: 7, Created: 2, Completed: 2)));

        var action = GetAction(recorder, CollectionRuntimeAction.BackfillRecovery);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-08T03:00:01Z"), action.LastStartedAtUtc);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-08T03:00:03.500Z"), action.LastCompletedAtUtc);
        Assert.AreEqual(7, action.InspectedCount);
        Assert.AreEqual(2, action.CreatedCount);
        Assert.AreEqual(2, action.CompletedCount);
        Assert.AreEqual(0, action.ConsecutiveErrors);
    }

    [TestMethod]
    public void Cancellation_CompletesWithoutChangingSuccessProgressOrErrorCount()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-08T03:00:00Z"));
        var recorder = CreateRecorder(clock);
        var successful = recorder.BeginCycle(CollectionRuntimeAction.Alerts)!.Value;
        Assert.IsTrue(recorder.RecordProgress(successful));
        Assert.IsTrue(recorder.CompleteCycle(successful, CollectionRuntimeReason.NoDueWork,
            new(Created: 5)));
        var lastSuccess = GetAction(recorder, CollectionRuntimeAction.Alerts);
        Assert.IsNotNull(lastSuccess.LastProgressAtUtc);

        var failed = recorder.BeginCycle(CollectionRuntimeAction.Alerts)!.Value;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsTrue(recorder.FailCycle(failed, new(Inspected: 1)));
        var beforeCancel = GetAction(recorder, CollectionRuntimeAction.Alerts);
        Assert.AreEqual(CollectionRuntimeState.Error, beforeCancel.State);
        Assert.AreEqual(CollectionRuntimeReason.Error, beforeCancel.Reason);
        Assert.AreEqual(1, beforeCancel.ConsecutiveErrors);

        var cancelled = recorder.BeginCycle(CollectionRuntimeAction.Alerts)!.Value;
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.IsTrue(recorder.CancelCycle(cancelled));

        var afterCancel = GetAction(recorder, CollectionRuntimeAction.Alerts);
        Assert.AreEqual(CollectionRuntimeState.Waiting, afterCancel.State);
        Assert.IsNull(afterCancel.Reason);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-08T03:00:02.400Z"), afterCancel.LastCompletedAtUtc);
        Assert.AreEqual(400, afterCancel.LastDurationMilliseconds);
        Assert.AreEqual(lastSuccess.LastSuccessfulCycleAtUtc, afterCancel.LastSuccessfulCycleAtUtc);
        Assert.AreEqual(lastSuccess.LastProgressAtUtc, afterCancel.LastProgressAtUtc);
        Assert.AreEqual(0, afterCancel.CreatedCount,
            "Counts describe the cancelled latest cycle rather than retaining a prior cycle's values.");
        Assert.AreEqual(1, afterCancel.ConsecutiveErrors);

        var recovered = recorder.BeginCycle(CollectionRuntimeAction.Alerts)!.Value;
        Assert.IsTrue(recorder.CompleteCycle(recovered, CollectionRuntimeReason.NoDueWork));
        Assert.AreEqual(0, GetAction(recorder, CollectionRuntimeAction.Alerts).ConsecutiveErrors);
    }

    [TestMethod]
    public void Failure_PreservesProgressRecordedAfterCommittedPartialWork()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-08T03:00:00Z"));
        var recorder = CreateRecorder(clock);
        var token = recorder.BeginCycle(CollectionRuntimeAction.BackfillRecovery)!.Value;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsTrue(recorder.RecordProgress(token));
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.IsTrue(recorder.FailCycle(token, new(Inspected: 7, Created: 3, Completed: 2)));

        var action = GetAction(recorder, CollectionRuntimeAction.BackfillRecovery);
        Assert.AreEqual(CollectionRuntimeState.Error, action.State);
        Assert.AreEqual(CollectionRuntimeReason.Error, action.Reason);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-08T03:00:02Z"), action.LastProgressAtUtc);
        Assert.AreEqual(7, action.InspectedCount);
        Assert.AreEqual(3, action.CreatedCount);
        Assert.AreEqual(2, action.CompletedCount);
        Assert.AreEqual(1, action.ConsecutiveErrors);
    }

    [TestMethod]
    public void DisabledActionCannotBeginCycle_AndInvalidConfigurationsAreRejected()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-08T03:00:00Z"));
        var recorder = CreateRecorder(clock, disabled: CollectionRuntimeAction.DeadLetterReconciliation);

        Assert.IsNull(recorder.BeginCycle(CollectionRuntimeAction.DeadLetterReconciliation));
        Assert.IsFalse(recorder.CompleteCycle(new(CollectionRuntimeAction.DeadLetterReconciliation, 1), null));

        var missing = Configurations().Where(x => x.Action != CollectionRuntimeAction.Alerts);
        Assert.Throws<ArgumentException>(() => new CollectionRuntimeStatusRecorder(missing, clock));
        var duplicate = Configurations().Append(Configurations().First());
        Assert.Throws<ArgumentException>(() => new CollectionRuntimeStatusRecorder(duplicate, clock));
        var zeroInterval = Configurations().Select(x => x.Action == CollectionRuntimeAction.Dispatcher
            ? x with { EffectiveInterval = TimeSpan.Zero }
            : x);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionRuntimeStatusRecorder(zeroInterval, clock));
    }

    [TestMethod]
    public void TransitionPolicy_IsPureAndRejectsStaleOrInvalidEvents()
    {
        var initial = new CollectionRuntimeActionSnapshot(CollectionRuntimeAction.Dispatcher,
            Enabled: true, EffectiveInterval: TimeSpan.FromSeconds(1), CollectionRuntimeState.NotObserved,
            Reason: null, LastStartedAtUtc: null, LastCompletedAtUtc: null,
            LastSuccessfulCycleAtUtc: null, LastProgressAtUtc: null, LastDurationMilliseconds: null,
            InspectedCount: 0, CreatedCount: 0, ReclaimedCount: 0, SentCount: 0, CompletedCount: 0,
            ConsecutiveErrors: 0, CurrentCycleToken: 0, ActiveCycleStartTimestamp: null);
        var startedAt = DateTimeOffset.Parse("2026-10-08T03:00:00Z");

        var running = CollectionRuntimeStatusTransitionPolicy.Begin(initial, 1, startedAt, 100);
        var stale = CollectionRuntimeStatusTransitionPolicy.Complete(running, 0, startedAt, 1,
            CollectionRuntimeReason.NoDueWork, new());
        var staleCancel = CollectionRuntimeStatusTransitionPolicy.Cancel(running, 0, startedAt, 1, new());
        var invalid = CollectionRuntimeStatusTransitionPolicy.Complete(running, 1, startedAt, 1,
            CollectionRuntimeReason.NoDueWork, new(Inspected: -1));
        var contradictory = CollectionRuntimeStatusTransitionPolicy.Complete(running, 1, startedAt, 1,
            CollectionRuntimeReason.Error, new());
        var invalidCancel = CollectionRuntimeStatusTransitionPolicy.Cancel(running, 1, startedAt, 1,
            new(Created: -1));
        var cancelled = CollectionRuntimeStatusTransitionPolicy.Cancel(running, 1, startedAt, 1,
            new(Inspected: 3, Created: 1));
        var completed = CollectionRuntimeStatusTransitionPolicy.Complete(running, 1, startedAt, 1,
            CollectionRuntimeReason.NoDueWork, new(Inspected: 2));

        Assert.AreEqual(CollectionRuntimeState.NotObserved, initial.State);
        Assert.AreEqual(CollectionRuntimeState.Running, running.State);
        Assert.AreSame(running, stale);
        Assert.AreSame(running, staleCancel);
        Assert.AreSame(running, invalid);
        Assert.AreSame(running, contradictory);
        Assert.AreSame(running, invalidCancel);
        Assert.AreEqual(CollectionRuntimeState.Waiting, cancelled.State);
        Assert.AreEqual(3, cancelled.InspectedCount);
        Assert.AreEqual(1, cancelled.CreatedCount);
        Assert.IsNull(cancelled.LastSuccessfulCycleAtUtc);
        Assert.AreEqual(CollectionRuntimeState.Waiting, completed.State);
        Assert.AreEqual(2, completed.InspectedCount);
    }

    private static CollectionRuntimeStatusRecorder CreateRecorder(TestTimeProvider clock,
        CollectionRuntimeAction? disabled = null) =>
        new(Configurations(disabled), clock);

    private static IEnumerable<CollectionRuntimeActionConfiguration> Configurations(
        CollectionRuntimeAction? disabled = null) => Actions.Select(action =>
        new CollectionRuntimeActionConfiguration(action, action != disabled,
            action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromSeconds(1)));

    private static CollectionRuntimeActionStatusDto GetAction(CollectionRuntimeStatusRecorder recorder,
        CollectionRuntimeAction action) => recorder.GetSnapshot().Runtime.Actions.Single(x => x.Action == action);

    private sealed class TestTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed)
        {
            _utcNow = _utcNow.Add(elapsed);
            _timestamp += elapsed.Ticks;
        }
    }
}
