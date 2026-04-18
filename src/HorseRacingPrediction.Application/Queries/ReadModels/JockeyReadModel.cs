using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class JockeyReadModel : IReadModel,
    IAmReadModelFor<JockeyAggregate, JockeyId, JockeyRegistered>,
    IAmReadModelFor<JockeyAggregate, JockeyId, JockeyProfileUpdated>,
    IAmReadModelFor<JockeyAggregate, JockeyId, JockeyAliasMerged>,
    IAmReadModelFor<JockeyAggregate, JockeyId, JockeyDataCorrected>
{
    private readonly List<JockeyAliasEntry> _aliases = new();

    public string JockeyId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? AffiliationCode { get; private set; }
    public IReadOnlyList<JockeyAliasEntry> Aliases => _aliases.AsReadOnly();

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<JockeyAggregate, JockeyId, JockeyRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        JockeyId = domainEvent.AggregateIdentity.Value;
        DisplayName = e.DisplayName;
        NormalizedName = e.NormalizedName;
        AffiliationCode = e.AffiliationCode;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<JockeyAggregate, JockeyId, JockeyProfileUpdated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.DisplayName != null) DisplayName = e.DisplayName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.AffiliationCode != null) AffiliationCode = e.AffiliationCode;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<JockeyAggregate, JockeyId, JockeyAliasMerged> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _aliases.Add(new JockeyAliasEntry(e.AliasType, e.AliasValue, e.SourceName, e.IsPrimary));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<JockeyAggregate, JockeyId, JockeyDataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.DisplayName != null) DisplayName = e.DisplayName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.AffiliationCode != null) AffiliationCode = e.AffiliationCode;
        return Task.CompletedTask;
    }
}
