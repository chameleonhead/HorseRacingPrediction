using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;
namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class CollectTrainerProfileCommandHandler : CommandHandler<TrainerAggregate, TrainerId, CollectTrainerProfileCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, CollectTrainerProfileCommand command, CancellationToken token)
    { aggregate.CollectJraProfile(command.Profile); return Task.CompletedTask; }
}
