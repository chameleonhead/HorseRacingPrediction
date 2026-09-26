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
        if (domainEvent is IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> repair)
        {
            foreach (var entry in repair.AggregateEvent.Entries)
                if (entry.HorseId is { } subject) _entryToHorse[entry.EntryId] = subject;
            foreach (var subject in repair.AggregateEvent.PreviousEntries.Concat(repair.AggregateEvent.Entries)
                .Select(x => x.HorseId).Where(x => x is not null).Distinct())
                yield return subject!;
            yield break;
        }
        if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryRegistered> entryEvent)
        {

            if (entryEvent.AggregateEvent.PreviousHorseId is { } previous && previous != entryEvent.AggregateEvent.HorseId) yield return previous;
            _entryToHorse[entryEvent.AggregateEvent.EntryId] = entryEvent.AggregateEvent.HorseId;
            yield return entryEvent.AggregateEvent.HorseId;
        }
        else if (domainEvent is IDomainEvent<RaceAggregate, RaceId, EntryResultDeclared> resultEvent)
        {
            if (resultEvent.AggregateEvent.HorseId is { } collectedId) yield return collectedId;
            else if (_entryToHorse.TryGetValue(resultEvent.AggregateEvent.EntryId, out var horseId))
                yield return horseId;
        }
    }
}
