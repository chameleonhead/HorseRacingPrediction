using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class HorseWeightHistoryLocator : IReadModelLocator
{
    public IEnumerable<string> GetReadModelIds(IDomainEvent domainEvent)
    {
        if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryRegistered> entryEvent)
        {
            yield return entryEvent.AggregateEvent.HorseId;
        }
    }
}
