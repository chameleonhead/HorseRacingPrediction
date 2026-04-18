using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Memos;

public sealed class MemoSubjectsChanged : AggregateEvent<MemoAggregate, MemoId>
{
    public MemoSubjectsChanged(
        IReadOnlyList<MemoSubject> addedSubjects,
        IReadOnlyList<MemoSubject> removedSubjects,
        IReadOnlyList<MemoSubject> allSubjects,
        string? authorId,
        string memoType,
        string content,
        DateTimeOffset createdAt,
        IReadOnlyList<MemoLink> links)
    {
        AddedSubjects = addedSubjects;
        RemovedSubjects = removedSubjects;
        AllSubjects = allSubjects;
        AuthorId = authorId;
        MemoType = memoType;
        Content = content;
        CreatedAt = createdAt;
        Links = links;
    }

    public IReadOnlyList<MemoSubject> AddedSubjects { get; }
    public IReadOnlyList<MemoSubject> RemovedSubjects { get; }

    /// <summary>Complete subject list after the change; used by the read-model locator.</summary>
    public IReadOnlyList<MemoSubject> AllSubjects { get; }

    /// <summary>Current memo snapshot; required to populate newly added subject read models.</summary>
    public string? AuthorId { get; }
    public string MemoType { get; }
    public string Content { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<MemoLink> Links { get; }
}
