using System.Collections.Concurrent;
using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class JockeyRaceHistoryLocator : IReadModelLocator
{
    private readonly ConcurrentDictionary<string, string> _entryToJockey = new();

    public IEnumerable<string> GetReadModelIds(IDomainEvent domainEvent)
    {
        if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryRegistered> entryEvent)
        {
            var jockeyId = entryEvent.AggregateEvent.JockeyId;
            if (jockeyId != null)
            {
                _entryToJockey[entryEvent.AggregateEvent.EntryId] = jockeyId;
                yield return jockeyId;
            }
        }
        else if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryResultDeclared> resultEvent)
        {
            if (_entryToJockey.TryGetValue(resultEvent.AggregateEvent.EntryId, out var jockeyId))
                yield return jockeyId;
        }
    }
}
