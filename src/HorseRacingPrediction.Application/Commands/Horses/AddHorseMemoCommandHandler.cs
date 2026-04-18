using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class AddHorseMemoCommandHandler : CommandHandler<HorseAggregate, HorseId, AddHorseMemoCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, AddHorseMemoCommand command, CancellationToken cancellationToken)
    {
        aggregate.AddMemo(command.MemoId, command.AuthorId, command.MemoType,
            command.Content, command.CreatedAt, command.Links);
        return Task.CompletedTask;
    }
}
