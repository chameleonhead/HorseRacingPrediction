using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Memos;

public sealed class MemoUpdated : AggregateEvent<MemoAggregate, MemoId>
{
    public MemoUpdated(string? memoType, string? content,
        IReadOnlyList<MemoLink>? links, IReadOnlyList<MemoSubject> currentSubjects)
    {
        MemoType = memoType;
        Content = content;
        Links = links;
        CurrentSubjects = currentSubjects;
    }

    public string? MemoType { get; }
    public string? Content { get; }
    public IReadOnlyList<MemoLink>? Links { get; }

    /// <summary>Subjects at the time of the update; used by the read-model locator.</summary>
    public IReadOnlyList<MemoSubject> CurrentSubjects { get; }
}
