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

            if (entryEvent.AggregateEvent.PreviousHorseId is { } previous && previous != entryEvent.AggregateEvent.HorseId) yield return previous;
            yield return entryEvent.AggregateEvent.HorseId;
        }
        else if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryCollectedDataUpdated> updateEvent)
        {
            yield return updateEvent.AggregateEvent.HorseId;
        }
    }
}
