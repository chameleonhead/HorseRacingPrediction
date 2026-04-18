using EventFlow.Commands;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Commands.Memos;

public sealed class DeleteMemoCommandHandler : CommandHandler<MemoAggregate, MemoId, DeleteMemoCommand>
{
    public override Task ExecuteAsync(MemoAggregate aggregate, DeleteMemoCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeleteMemo();
        return Task.CompletedTask;
    }
}
