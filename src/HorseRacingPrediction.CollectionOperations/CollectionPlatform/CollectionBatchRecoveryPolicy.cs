using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

internal sealed record CollectionBatchClassification(CollectionBatchKind Kind, string? ReviewReason)
{
    public bool IsReady => Kind != CollectionBatchKind.Unknown && ReviewReason is null;
}

internal sealed record CollectionBatchReasonCount(CollectionReason Reason, long Count);

internal sealed record CollectionBatchRecoveryPlan(CollectionBatchKind Kind, DateOnly StartDate,
    IReadOnlyList<DateOnly> Dates, DateOnly? NextDate, bool Completes, string? ReviewReason);

internal static class CollectionBatchRecoveryPolicy
{
    internal const int MaxDatesPerBatch = 7;
    private static readonly HashSet<string> AllowedReviewReasons = new(StringComparer.Ordinal)
    {
        "NoRootEvidence", "MixedOrUnsupportedReasons", "RootShapeOrRangeMismatch"
    };

    public static CollectionBatchClassification ClassifyAggregated(
        IReadOnlyList<CollectionBatchReasonCount> reasonCounts, string? evidenceIssue)
    {
        if (reasonCounts.Count == 0)
            return new(CollectionBatchKind.Unknown, "NoRootEvidence");
        if (reasonCounts.Count != 1 || reasonCounts[0].Reason is not
            (CollectionReason.Backfill or CollectionReason.PeriodRecollection))
            return new(CollectionBatchKind.Unknown, "MixedOrUnsupportedReasons");
        if (evidenceIssue is not null)
            return new(CollectionBatchKind.Unknown, AllowedReviewReasons.Contains(evidenceIssue)
                ? evidenceIssue : "RootShapeOrRangeMismatch");
        return new(reasonCounts[0].Reason == CollectionReason.Backfill
            ? CollectionBatchKind.Backfill : CollectionBatchKind.PeriodRecollection, null);
    }

    public static CollectionBatchRecoveryPlan PlanChunk(CollectionBatchKind kind, DateOnly from,
        DateOnly to, DateOnly nextDate, int maxDates = MaxDatesPerBatch)
    {
        if (kind is not (CollectionBatchKind.Backfill or CollectionBatchKind.PeriodRecollection))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (from > to || nextDate < from || nextDate > to)
            throw new ArgumentOutOfRangeException(nameof(nextDate));
        if (maxDates is < 1 or > MaxDatesPerBatch) throw new ArgumentOutOfRangeException(nameof(maxDates));

        var dates = new List<DateOnly>(maxDates);
        var date = nextDate;
        while (dates.Count < maxDates)
        {
            dates.Add(date);
            if (date == to) break;
            date = date.AddDays(1);
        }
        var following = dates[^1] == to ? (DateOnly?)null : dates[^1].AddDays(1);
        return new(kind, nextDate, dates, following, following is null, null);
    }
}
