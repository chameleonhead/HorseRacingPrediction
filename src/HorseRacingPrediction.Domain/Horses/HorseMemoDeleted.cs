using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseMemoDeleted : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseMemoDeleted(string memoId)
    {
        MemoId = memoId;
    }

    public string MemoId { get; }
}
