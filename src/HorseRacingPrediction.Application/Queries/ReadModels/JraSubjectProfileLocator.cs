using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Trainers;
namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed class JraSubjectProfileLocator : IReadModelLocator
{
    public IEnumerable<string> GetReadModelIds(IDomainEvent domainEvent)
    {
        if(domainEvent is IDomainEvent<HorseAggregate,HorseId,HorseJraProfileCollected> horse) yield return horse.AggregateIdentity.Value;
        if(domainEvent is IDomainEvent<TrainerAggregate,TrainerId,TrainerJraProfileCollected> trainer) yield return trainer.AggregateIdentity.Value;
    }
}
