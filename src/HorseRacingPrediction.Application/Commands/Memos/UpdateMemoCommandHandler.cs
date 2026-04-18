using EventFlow.Commands;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Commands.Memos;

public sealed class UpdateMemoCommandHandler : CommandHandler<MemoAggregate, MemoId, UpdateMemoCommand>
{
    public override Task ExecuteAsync(MemoAggregate aggregate, UpdateMemoCommand command, CancellationToken cancellationToken)
    {
        aggregate.UpdateMemo(command.MemoType, command.Content, command.Links);
        return Task.CompletedTask;
    }
}
