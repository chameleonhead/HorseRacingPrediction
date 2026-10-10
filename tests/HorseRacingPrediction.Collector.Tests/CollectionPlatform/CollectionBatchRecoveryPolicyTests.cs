using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionBatchRecoveryPolicyTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 4, 11);

    [TestMethod]
    public void ClassifyAggregated_RequiresOneSupportedReasonAndValidatedRootEvidence()
    {
        var classified = CollectionBatchRecoveryPolicy.ClassifyAggregated(
            [new(CollectionReason.Backfill, 1)], null);
        Assert.AreEqual(CollectionBatchKind.Backfill, classified.Kind);
        Assert.IsTrue(classified.IsReady);

        Assert.AreEqual("NoRootEvidence",
            CollectionBatchRecoveryPolicy.ClassifyAggregated([], null).ReviewReason);
        Assert.AreEqual("MixedOrUnsupportedReasons", CollectionBatchRecoveryPolicy.ClassifyAggregated(
            [new(CollectionReason.Backfill, 2), new(CollectionReason.PeriodRecollection, 1)], null).ReviewReason);
        Assert.AreEqual("MixedOrUnsupportedReasons",
            CollectionBatchRecoveryPolicy.ClassifyAggregated([new(CollectionReason.Discovery, 1)], null).ReviewReason);
        Assert.AreEqual("RootShapeOrRangeMismatch",
            CollectionBatchRecoveryPolicy.ClassifyAggregated([new(CollectionReason.Backfill, 1)],
                "unrecognized-input").ReviewReason);
    }

    [TestMethod]
    public void Classify_DoesNotTreatIdentifierPrefixAsPositiveEvidence()
    {
        var result = CollectionBatchRecoveryPolicy.ClassifyAggregated(
            [new(CollectionReason.Discovery, 1)], null);
        Assert.AreEqual(CollectionBatchKind.Unknown, result.Kind);
        Assert.IsFalse(result.IsReady);
    }

    [TestMethod]
    public void PlanChunk_ProcessesAtMostSevenDatesAndNeverSkipsAResumeCursor()
    {
        var cursor = new DateOnly(2026, 4, 4);
        var plan = CollectionBatchRecoveryPolicy.PlanChunk(CollectionBatchKind.PeriodRecollection,
            From, To, cursor);

        Assert.AreEqual(cursor, plan.StartDate);
        Assert.AreEqual(7, plan.Dates.Count);
        Assert.AreEqual(cursor, plan.Dates[0]);
        Assert.AreEqual(cursor.AddDays(6), plan.Dates[^1]);
        Assert.AreEqual(cursor.AddDays(7), plan.NextDate);
        Assert.IsFalse(plan.Completes);
    }

    [TestMethod]
    public void PlanChunk_FinishesOnlyAfterInclusiveEndDate()
    {
        var plan = CollectionBatchRecoveryPolicy.PlanChunk(CollectionBatchKind.Backfill,
            From, To, To);

        CollectionAssert.AreEqual(new[] { To }, plan.Dates.ToArray());
        Assert.IsNull(plan.NextDate);
        Assert.IsTrue(plan.Completes);
    }

    [TestMethod]
    public void PlanChunk_HandlesDateOnlyMaximumWithoutIncrementOverflow()
    {
        var plan = CollectionBatchRecoveryPolicy.PlanChunk(CollectionBatchKind.Backfill,
            DateOnly.MaxValue, DateOnly.MaxValue, DateOnly.MaxValue);

        CollectionAssert.AreEqual(new[] { DateOnly.MaxValue }, plan.Dates.ToArray());
        Assert.IsTrue(plan.Completes);
        Assert.IsNull(plan.NextDate);
    }
}
