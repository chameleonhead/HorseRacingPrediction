using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Memos;

public sealed class MemoState : AggregateState<MemoAggregate, MemoId, MemoState>,
    IApply<MemoCreated>,
    IApply<MemoUpdated>,
    IApply<MemoDeleted>,
    IApply<MemoSubjectsChanged>
{
    private readonly List<MemoSubject> _subjects = new();
    private readonly List<MemoLink> _links = new();

    public bool IsCreated { get; private set; }
    public bool IsDeleted { get; private set; }
    public string? AuthorId { get; private set; }
    public string MemoType { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<MemoSubject> Subjects => _subjects.AsReadOnly();
    public IReadOnlyList<MemoLink> Links => _links.AsReadOnly();

    public void Apply(MemoCreated e)
    {
        IsCreated = true;
        AuthorId = e.AuthorId;
        MemoType = e.MemoType;
        Content = e.Content;
        CreatedAt = e.CreatedAt;
        _subjects.AddRange(e.Subjects);
        _links.AddRange(e.Links);
    }

    public void Apply(MemoUpdated e)
    {
        if (e.MemoType != null) MemoType = e.MemoType;
        if (e.Content != null) Content = e.Content;
        if (e.Links != null)
        {
            _links.Clear();
            _links.AddRange(e.Links);
        }
    }

    public void Apply(MemoDeleted e)
    {
        IsDeleted = true;
    }

    public void Apply(MemoSubjectsChanged e)
    {
        _subjects.Clear();
        _subjects.AddRange(e.AllSubjects);
    }
}
