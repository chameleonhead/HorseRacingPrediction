using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class UpdateHorseMemoCommandHandler : CommandHandler<HorseAggregate, HorseId, UpdateHorseMemoCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, UpdateHorseMemoCommand command, CancellationToken cancellationToken)
    {
        aggregate.UpdateMemo(command.MemoId, command.MemoType, command.Content, command.Links);
        return Task.CompletedTask;
    }
}
