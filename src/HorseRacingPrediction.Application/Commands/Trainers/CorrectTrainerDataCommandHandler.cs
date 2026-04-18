using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class CorrectTrainerDataCommandHandler : CommandHandler<TrainerAggregate, TrainerId, CorrectTrainerDataCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, CorrectTrainerDataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectData(command.DisplayName, command.NormalizedName, command.AffiliationCode, command.Reason);
        return Task.CompletedTask;
    }
}
