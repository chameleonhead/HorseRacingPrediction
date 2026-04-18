using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class HorseWeightHistoryReadModel : IReadModel,
    IAmReadModelFor<RaceAggregate, RaceId, EntryRegistered>
{
    private readonly List<HorseWeightEntry> _weightHistory = new();

    public string HorseId { get; private set; } = string.Empty;
    public IReadOnlyList<HorseWeightEntry> WeightHistory => _weightHistory.AsReadOnly();

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        HorseId = e.HorseId;
        _weightHistory.Add(new HorseWeightEntry(
            domainEvent.AggregateIdentity.Value,
            e.EntryId,
            domainEvent.Timestamp,
            e.DeclaredWeight,
            e.DeclaredWeightDiff));
        return Task.CompletedTask;
    }
}
