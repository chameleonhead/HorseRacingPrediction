using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class RegisterTrainerCommandHandler : CommandHandler<TrainerAggregate, TrainerId, RegisterTrainerCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, RegisterTrainerCommand command, CancellationToken cancellationToken)
    {
        aggregate.RegisterTrainer(command.DisplayName, command.NormalizedName, command.AffiliationCode);
        return Task.CompletedTask;
    }
}
