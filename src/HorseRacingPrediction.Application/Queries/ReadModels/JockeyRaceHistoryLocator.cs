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
        if (domainEvent is IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> repair)
        {
            foreach (var entry in repair.AggregateEvent.Entries)
                if (entry.JockeyId is { } subject) _entryToJockey[entry.EntryId] = subject;
            foreach (var subject in repair.AggregateEvent.PreviousEntries.Concat(repair.AggregateEvent.Entries)
                .Select(x => x.JockeyId).Where(x => x is not null).Distinct())
                yield return subject!;
            yield break;
        }
        if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryRegistered> entryEvent)
        {

            if (entryEvent.AggregateEvent.PreviousJockeyId is { } previous && previous != entryEvent.AggregateEvent.JockeyId) yield return previous;
            var jockeyId = entryEvent.AggregateEvent.JockeyId;
            if (jockeyId != null)
            {
                _entryToJockey[entryEvent.AggregateEvent.EntryId] = jockeyId;
                yield return jockeyId;
            }
        }
        else if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryResultDeclared> resultEvent)
        {
            if (resultEvent.AggregateEvent.JockeyId is { } collectedId) yield return collectedId;
            else if (_entryToJockey.TryGetValue(resultEvent.AggregateEvent.EntryId, out var jockeyId))
                yield return jockeyId;
        }
    }
}
