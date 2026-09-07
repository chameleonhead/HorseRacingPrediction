using EventFlow.Commands;
using HorseRacingPrediction.Domain;
using HorseRacingPrediction.Domain.Trainers;
namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class CollectTrainerProfileCommand(TrainerId id, CollectedSubjectProfile profile) : Command<TrainerAggregate, TrainerId>(id)
{
    public CollectedSubjectProfile Profile { get; } = profile;
}
