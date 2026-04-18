using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class MemoBySubjectReadModel : IReadModel,
    IAmReadModelFor<MemoAggregate, MemoId, MemoCreated>,
    IAmReadModelFor<MemoAggregate, MemoId, MemoUpdated>,
    IAmReadModelFor<MemoAggregate, MemoId, MemoDeleted>,
    IAmReadModelFor<MemoAggregate, MemoId, MemoSubjectsChanged>
{
    private readonly List<MemoSnapshot> _memos = new();

    public string SubjectKey { get; private set; } = string.Empty;
    public IReadOnlyList<MemoSnapshot> Memos => _memos.AsReadOnly();

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<MemoAggregate, MemoId, MemoCreated> domainEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(SubjectKey))
            SubjectKey = context.ReadModelId;

        var e = domainEvent.AggregateEvent;
        var memoId = domainEvent.AggregateIdentity.Value;

        _memos.Add(BuildSnapshot(memoId, e.AuthorId, e.MemoType, e.Content, e.CreatedAt,
            e.Subjects, e.Links));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<MemoAggregate, MemoId, MemoUpdated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var memoId = domainEvent.AggregateIdentity.Value;
        var index = _memos.FindIndex(m => m.MemoId == memoId);
        if (index < 0) return Task.CompletedTask;

        var existing = _memos[index];
        var updatedLinks = e.Links != null
            ? ToLinkSnapshots(e.Links)
            : existing.Links;

        _memos[index] = existing with
        {
            MemoType = e.MemoType ?? existing.MemoType,
            Content = e.Content ?? existing.Content,
            Links = updatedLinks
        };
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<MemoAggregate, MemoId, MemoDeleted> domainEvent,
        CancellationToken cancellationToken)
    {
        var memoId = domainEvent.AggregateIdentity.Value;
        _memos.RemoveAll(m => m.MemoId == memoId);
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<MemoAggregate, MemoId, MemoSubjectsChanged> domainEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(SubjectKey))
            SubjectKey = context.ReadModelId;

        var e = domainEvent.AggregateEvent;
        var memoId = domainEvent.AggregateIdentity.Value;

        var isRemoved = e.RemovedSubjects.Any(s =>
            MemoBySubjectLocator.MakeKey(s.SubjectType, s.SubjectId) == SubjectKey);

        if (isRemoved)
        {
            _memos.RemoveAll(m => m.MemoId == memoId);
            return Task.CompletedTask;
        }

        var index = _memos.FindIndex(m => m.MemoId == memoId);
        var newSnapshot = BuildSnapshot(memoId, e.AuthorId, e.MemoType, e.Content, e.CreatedAt,
            e.AllSubjects, e.Links);

        if (index >= 0)
            _memos[index] = newSnapshot;
        else
            _memos.Add(newSnapshot);

        return Task.CompletedTask;
    }

    private static MemoSnapshot BuildSnapshot(string memoId, string? authorId, string memoType,
        string content, DateTimeOffset createdAt,
        IReadOnlyList<MemoSubject> subjects, IReadOnlyList<MemoLink> links)
    {
        return new MemoSnapshot(
            memoId, authorId, memoType, content, createdAt,
            subjects.Select(s => new MemoSubjectSnapshot(s.SubjectType.ToString(), s.SubjectId)).ToList(),
            ToLinkSnapshots(links));
    }

    private static IReadOnlyList<MemoLinkSnapshot> ToLinkSnapshots(IReadOnlyList<MemoLink> links)
        => links.Select(l => new MemoLinkSnapshot(l.LinkId, l.LinkType.ToString(), l.Title, l.Url, l.StorageKey))
                .ToList();
}
