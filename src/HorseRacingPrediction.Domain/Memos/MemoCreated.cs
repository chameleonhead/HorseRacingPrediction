using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Memos;

public sealed class MemoCreated : AggregateEvent<MemoAggregate, MemoId>
{
    public MemoCreated(string? authorId, string memoType, string content,
        DateTimeOffset createdAt, IReadOnlyList<MemoSubject> subjects,
        IReadOnlyList<MemoLink> links)
    {
        AuthorId = authorId;
        MemoType = memoType;
        Content = content;
        CreatedAt = createdAt;
        Subjects = subjects;
        Links = links;
    }

    public string? AuthorId { get; }
    public string MemoType { get; }
    public string Content { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<MemoSubject> Subjects { get; }
    public IReadOnlyList<MemoLink> Links { get; }
}
