using System.Collections.Concurrent;
using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class HorseRaceHistoryLocator : IReadModelLocator
{
    private readonly ConcurrentDictionary<string, string> _entryToHorse = new();

    public IEnumerable<string> GetReadModelIds(IDomainEvent domainEvent)
    {
        if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryRegistered> entryEvent)
        {
            _entryToHorse[entryEvent.AggregateEvent.EntryId] = entryEvent.AggregateEvent.HorseId;
            yield return entryEvent.AggregateEvent.HorseId;
        }
        else if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryResultDeclared> resultEvent)
        {
            if (_entryToHorse.TryGetValue(resultEvent.AggregateEvent.EntryId, out var horseId))
                yield return horseId;
        }
    }
}
