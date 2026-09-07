using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class HorseWeightHistoryReadModel : IReadModel,
    IAmReadModelFor<RaceAggregate, RaceId, EntryRegistered>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryCollectedDataUpdated>
{
    public string HorseId { get; private set; } = string.Empty;
    public List<HorseWeightEntry> WeightHistory { get; private set; } = [];

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        HorseId = e.HorseId;
        WeightHistory.Add(new HorseWeightEntry(
            domainEvent.AggregateIdentity.Value,
            e.EntryId,
            domainEvent.Timestamp,
            e.DeclaredWeight,
            e.DeclaredWeightDiff));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryCollectedDataUpdated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var index = WeightHistory.FindIndex(x => x.EntryId == e.EntryId);
        if (index >= 0)
        {
            var current = WeightHistory[index];
            WeightHistory[index] = current with
            {
                DeclaredWeight = e.DeclaredWeight ?? current.DeclaredWeight,
                DeclaredWeightDiff = e.DeclaredWeightDiff ?? current.DeclaredWeightDiff,
            };
        }
        return Task.CompletedTask;
    }
}
