using EventFlow.Commands;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Commands.Memos;

public sealed class DeleteMemoCommand : Command<MemoAggregate, MemoId>
{
    public DeleteMemoCommand(MemoId aggregateId)
        : base(aggregateId)
    {
    }
}
