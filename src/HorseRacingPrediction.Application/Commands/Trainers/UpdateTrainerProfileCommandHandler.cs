using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class UpdateTrainerProfileCommandHandler : CommandHandler<TrainerAggregate, TrainerId, UpdateTrainerProfileCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, UpdateTrainerProfileCommand command, CancellationToken cancellationToken)
    {
        aggregate.UpdateProfile(command.DisplayName, command.NormalizedName, command.AffiliationCode);
        return Task.CompletedTask;
    }
}
