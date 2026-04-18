using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Memos;

public sealed class MemoDeleted : AggregateEvent<MemoAggregate, MemoId>
{
    public MemoDeleted(IReadOnlyList<MemoSubject> subjects)
    {
        Subjects = subjects;
    }

    /// <summary>Subjects at the time of deletion; used by the read-model locator.</summary>
    public IReadOnlyList<MemoSubject> Subjects { get; }
}
