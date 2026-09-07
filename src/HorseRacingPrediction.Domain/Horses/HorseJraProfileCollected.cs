using EventFlow.Aggregates;
namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseJraProfileCollected(CollectedSubjectProfile profile) : AggregateEvent<HorseAggregate, HorseId>
{
    public CollectedSubjectProfile Profile { get; } = profile;
}
