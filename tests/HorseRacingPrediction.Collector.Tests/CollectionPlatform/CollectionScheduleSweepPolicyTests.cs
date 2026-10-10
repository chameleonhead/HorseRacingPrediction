using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionScheduleSweepPolicyTests
{
    [TestMethod]
    public void Decide_ProducesStableCursorAndEligibilityForSnapshot()
    {
        var dueAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));
        var candidate = Candidate(dueAt, resourcePk: 501, definition: "horse-profile");
        var schedule = new CollectionSchedule(true, dueAt.AddMinutes(30), CollectionPriority.Normal,
            CollectionLane.Normal, "profile-refresh");

        var decision = CollectionScheduleSweepPolicy.Decide(candidate, schedule, dueAt);

        Assert.IsTrue(decision.ShouldCollect);
        Assert.AreEqual("eligible", decision.Reason);
        Assert.AreEqual(new CollectionScheduleCursor(dueAt, 501, "horse-profile"), decision.NextCursor);
        Assert.AreEqual(dueAt, candidate.State.NextCollectionAt, "The pure decision must not mutate its input.");
    }

    [TestMethod]
    [DataRow(CollectionStateStatus.Failed, false, false, "failed")]
    [DataRow(CollectionStateStatus.Collecting, false, false, "collecting")]
    [DataRow(CollectionStateStatus.RefreshDue, true, false, "active")]
    [DataRow(CollectionStateStatus.RefreshDue, false, true, "repair-held")]
    public void Decide_SkipsUnsafeOrHeldSnapshotWithProgress(
        CollectionStateStatus status, bool active, bool held, string expectedReason)
    {
        var candidate = Candidate(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9)), 19,
            status: status, active: active);
        var schedule = new CollectionSchedule(true, null, CollectionPriority.Normal,
            CollectionLane.Normal, "eligible-policy");

        var decision = CollectionScheduleSweepPolicy.Decide(candidate, schedule,
            candidate.State.NextCollectionAt!.Value, held);

        Assert.IsFalse(decision.ShouldCollect);
        Assert.AreEqual(expectedReason, decision.Reason);
        Assert.AreEqual(CollectionScheduleSweepPolicy.CursorAfter(candidate), decision.NextCursor);
    }

    [TestMethod]
    public void Decide_PreservesPolicySkipReasonAndCursor()
    {
        var candidate = Candidate(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9)), 20);
        var decision = CollectionScheduleSweepPolicy.Decide(candidate,
            new(false, null, CollectionPriority.Background, CollectionLane.Background, "immutable"),
            candidate.State.NextCollectionAt!.Value);

        Assert.IsFalse(decision.ShouldCollect);
        Assert.AreEqual("immutable", decision.Reason);
        Assert.AreEqual(CollectionScheduleSweepPolicy.CursorAfter(candidate), decision.NextCursor);
    }

    [TestMethod]
    public void Decide_SkipsCandidateRetimedBeyondExplicitCurrentTime()
    {
        var candidate = Candidate(new DateTimeOffset(2026, 10, 1, 13, 0, 0, TimeSpan.FromHours(9)), 21);
        var now = candidate.State.NextCollectionAt!.Value.AddMinutes(-1);

        var decision = CollectionScheduleSweepPolicy.Decide(candidate,
            new(true, null, CollectionPriority.Normal, CollectionLane.Normal, "eligible"), now);

        Assert.IsFalse(decision.ShouldCollect);
        Assert.AreEqual("no-longer-due", decision.Reason);
        Assert.AreEqual(CollectionScheduleSweepPolicy.CursorAfter(candidate), decision.NextCursor);
    }

    [TestMethod]
    public void CompletePage_RetainsHorizonUntilExhaustionThenWraps()
    {
        var horizon = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));
        var cursor = new CollectionScheduleCursor(horizon, 500, "horse-profile");
        var progress = new CollectionScheduleSweepProgress(horizon, cursor);

        var continued = CollectionScheduleSweepPolicy.CompletePage(progress, 500, 500);
        var wrapped = CollectionScheduleSweepPolicy.CompletePage(progress, 499, 500);

        Assert.AreEqual(progress, continued);
        Assert.AreEqual(new CollectionScheduleSweepProgress(null, null), wrapped);
    }

    private static CollectionScheduleCandidate Candidate(DateTimeOffset dueAt, long resourcePk,
        string definition = "test", CollectionStateStatus status = CollectionStateStatus.RefreshDue,
        bool active = false)
    {
        var resource = new ResourceKey(CollectionResourceType.Horse, "JRA", $"horse-{resourcePk}");
        var state = new CollectionStateSnapshot(resource, new(definition), 1, 2, dueAt.AddDays(-1), dueAt, status);
        return new(state, active, resourcePk);
    }
}
