using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class TrainerReadModel : IReadModel,
    IAmReadModelFor<TrainerAggregate, TrainerId, TrainerRegistered>,
    IAmReadModelFor<TrainerAggregate, TrainerId, TrainerProfileUpdated>,
    IAmReadModelFor<TrainerAggregate, TrainerId, TrainerAliasMerged>,
    IAmReadModelFor<TrainerAggregate, TrainerId, TrainerDataCorrected>
{
    private readonly List<TrainerAliasEntry> _aliases = new();

    public string TrainerId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? AffiliationCode { get; private set; }
    public IReadOnlyList<TrainerAliasEntry> Aliases => _aliases.AsReadOnly();

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<TrainerAggregate, TrainerId, TrainerRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        TrainerId = domainEvent.AggregateIdentity.Value;
        DisplayName = e.DisplayName;
        NormalizedName = e.NormalizedName;
        AffiliationCode = e.AffiliationCode;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<TrainerAggregate, TrainerId, TrainerProfileUpdated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.DisplayName != null) DisplayName = e.DisplayName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.AffiliationCode != null) AffiliationCode = e.AffiliationCode;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<TrainerAggregate, TrainerId, TrainerAliasMerged> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _aliases.Add(new TrainerAliasEntry(e.AliasType, e.AliasValue, e.SourceName, e.IsPrimary));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<TrainerAggregate, TrainerId, TrainerDataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.DisplayName != null) DisplayName = e.DisplayName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.AffiliationCode != null) AffiliationCode = e.AffiliationCode;
        return Task.CompletedTask;
    }
}
