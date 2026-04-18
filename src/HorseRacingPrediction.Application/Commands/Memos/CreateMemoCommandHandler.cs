using EventFlow.Commands;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Commands.Memos;

public sealed class CreateMemoCommandHandler : CommandHandler<MemoAggregate, MemoId, CreateMemoCommand>
{
    public override Task ExecuteAsync(MemoAggregate aggregate, CreateMemoCommand command, CancellationToken cancellationToken)
    {
        aggregate.CreateMemo(command.AuthorId, command.MemoType, command.Content,
            command.CreatedAt, command.Subjects, command.Links);
        return Task.CompletedTask;
    }
}
