using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class DeleteHorseMemoCommand : Command<HorseAggregate, HorseId>
{
    public DeleteHorseMemoCommand(HorseId aggregateId, string memoId)
        : base(aggregateId)
    {
        MemoId = memoId;
    }

    public string MemoId { get; }
}
