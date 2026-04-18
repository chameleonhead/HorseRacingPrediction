using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class HorseReadModel : IReadModel,
    IAmReadModelFor<HorseAggregate, HorseId, HorseRegistered>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseProfileUpdated>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseAliasMerged>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseDataCorrected>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseMemoAdded>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseMemoUpdated>,
    IAmReadModelFor<HorseAggregate, HorseId, HorseMemoDeleted>
{
    private readonly List<HorseAliasEntry> _aliases = new();
    private readonly List<HorseMemoSnapshot> _memos = new();

    public string HorseId { get; private set; } = string.Empty;
    public string RegisteredName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? SexCode { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public IReadOnlyList<HorseAliasEntry> Aliases => _aliases.AsReadOnly();
    public IReadOnlyList<HorseMemoSnapshot> Memos => _memos.AsReadOnly();

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

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<HorseAggregate, HorseId, HorseMemoAdded> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var links = e.Links
            .Select(l => new HorseMemoLinkSnapshot(l.LinkId, l.LinkType.ToString(), l.Title, l.Url, l.StorageKey))
            .ToList();
        _memos.Add(new HorseMemoSnapshot(e.MemoId, e.AuthorId, e.MemoType, e.Content, e.CreatedAt, links));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<HorseAggregate, HorseId, HorseMemoUpdated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var index = _memos.FindIndex(m => m.MemoId == e.MemoId);
        if (index < 0) return Task.CompletedTask;

        var existing = _memos[index];
        var updatedLinks = e.Links != null
            ? e.Links.Select(l => new HorseMemoLinkSnapshot(l.LinkId, l.LinkType.ToString(), l.Title, l.Url, l.StorageKey)).ToList()
            : existing.Links.ToList();
        _memos[index] = existing with
        {
            MemoType = e.MemoType ?? existing.MemoType,
            Content = e.Content ?? existing.Content,
            Links = updatedLinks
        };
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<HorseAggregate, HorseId, HorseMemoDeleted> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _memos.RemoveAll(m => m.MemoId == e.MemoId);
        return Task.CompletedTask;
    }
}
