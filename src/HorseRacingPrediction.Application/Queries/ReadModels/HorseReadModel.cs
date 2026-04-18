using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class HorseReadModel : IReadModel,
    IAmReadModelFor<HorseAggregate, HorseId, HorseRegistered>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseProfileUpdated>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseAliasMerged>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseDataCorrected>
{
    private readonly List<HorseAliasEntry> _aliases = new();

    public string HorseId { get; private set; } = string.Empty;
    public string RegisteredName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? SexCode { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public IReadOnlyList<HorseAliasEntry> Aliases => _aliases.AsReadOnly();

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<HorseAggregate, HorseId, HorseRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        HorseId = domainEvent.AggregateIdentity.Value;
        RegisteredName = e.RegisteredName;
        NormalizedName = e.NormalizedName;
        SexCode = e.SexCode;
        BirthDate = e.BirthDate;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<HorseAggregate, HorseId, HorseProfileUpdated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.RegisteredName != null) RegisteredName = e.RegisteredName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.SexCode != null) SexCode = e.SexCode;
        if (e.BirthDate.HasValue) BirthDate = e.BirthDate;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<HorseAggregate, HorseId, HorseAliasMerged> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _aliases.Add(new HorseAliasEntry(e.AliasType, e.AliasValue, e.SourceName, e.IsPrimary));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<HorseAggregate, HorseId, HorseDataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.RegisteredName != null) RegisteredName = e.RegisteredName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.SexCode != null) SexCode = e.SexCode;
        if (e.BirthDate.HasValue) BirthDate = e.BirthDate;
        return Task.CompletedTask;
    }
}
