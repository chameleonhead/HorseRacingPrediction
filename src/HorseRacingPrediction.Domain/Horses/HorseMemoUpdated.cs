using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseMemoUpdated : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseMemoUpdated(string memoId, string? memoType, string? content,
        IReadOnlyList<HorseMemoLink>? links)
    {
        MemoId = memoId;
        MemoType = memoType;
        Content = content;
        Links = links;
    }

    public string MemoId { get; }
    public string? MemoType { get; }
    public string? Content { get; }
    public IReadOnlyList<HorseMemoLink>? Links { get; }
}
