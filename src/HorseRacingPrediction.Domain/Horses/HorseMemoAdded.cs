using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseMemoAdded : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseMemoAdded(string memoId, string? authorId, string memoType, string content,
        DateTimeOffset createdAt, IReadOnlyList<HorseMemoLink> links)
    {
        MemoId = memoId;
        AuthorId = authorId;
        MemoType = memoType;
        Content = content;
        CreatedAt = createdAt;
        Links = links;
    }

    public string MemoId { get; }
    public string? AuthorId { get; }
    public string MemoType { get; }
    public string Content { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<HorseMemoLink> Links { get; }
}
