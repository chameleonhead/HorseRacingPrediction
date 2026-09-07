using EventFlow.Aggregates;
namespace HorseRacingPrediction.Domain.Trainers;

public sealed class TrainerJraProfileCollected(CollectedSubjectProfile profile) : AggregateEvent<TrainerAggregate, TrainerId>
{
    public CollectedSubjectProfile Profile { get; } = profile;
}
