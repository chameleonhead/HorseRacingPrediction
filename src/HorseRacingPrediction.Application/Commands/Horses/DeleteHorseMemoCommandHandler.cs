using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class DeleteHorseMemoCommandHandler : CommandHandler<HorseAggregate, HorseId, DeleteHorseMemoCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, DeleteHorseMemoCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeleteMemo(command.MemoId);
        return Task.CompletedTask;
    }
}
