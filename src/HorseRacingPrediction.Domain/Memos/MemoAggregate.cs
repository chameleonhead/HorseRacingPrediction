using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Memos;

public class MemoAggregate : AggregateRoot<MemoAggregate, MemoId>,
    IEmit<MemoCreated>,
    IEmit<MemoUpdated>,
    IEmit<MemoDeleted>,
    IEmit<MemoSubjectsChanged>
{
    private readonly MemoState _state = new();

    public MemoAggregate(MemoId id)
        : base(id)
    {
        Register(_state);
    }

    public void CreateMemo(string? authorId, string memoType, string content,
        DateTimeOffset createdAt, IReadOnlyList<MemoSubject> subjects,
        IReadOnlyList<MemoLink>? links = null)
    {
        if (_state.IsCreated)
            throw new InvalidOperationException("Memo is already created.");

        if (subjects.Count == 0)
            throw new InvalidOperationException("At least one subject is required.");

        Emit(new MemoCreated(authorId, memoType, content, createdAt, subjects,
            links ?? Array.Empty<MemoLink>()));
    }

    public void UpdateMemo(string? memoType = null, string? content = null,
        IReadOnlyList<MemoLink>? links = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Memo has not been created.");

        if (_state.IsDeleted)
            throw new InvalidOperationException("Memo has been deleted.");

        Emit(new MemoUpdated(memoType, content, links, _state.Subjects));
    }

    public void DeleteMemo()
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Memo has not been created.");

        if (_state.IsDeleted)
            throw new InvalidOperationException("Memo is already deleted.");

        Emit(new MemoDeleted(_state.Subjects));
    }

    public void ChangeSubjects(IReadOnlyList<MemoSubject> newSubjects)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Memo has not been created.");

        if (_state.IsDeleted)
            throw new InvalidOperationException("Memo has been deleted.");

        if (newSubjects.Count == 0)
            throw new InvalidOperationException("At least one subject is required.");

        var currentSet = _state.Subjects.ToHashSet();
        var newSet = newSubjects.ToHashSet();

        var added = newSubjects.Where(s => !currentSet.Contains(s)).ToList();
        var removed = _state.Subjects.Where(s => !newSet.Contains(s)).ToList();

        Emit(new MemoSubjectsChanged(added, removed, newSubjects,
            _state.AuthorId, _state.MemoType, _state.Content, _state.CreatedAt, _state.Links));
    }

    public void Apply(MemoCreated e) { }
    public void Apply(MemoUpdated e) { }
    public void Apply(MemoDeleted e) { }
    public void Apply(MemoSubjectsChanged e) { }
}
