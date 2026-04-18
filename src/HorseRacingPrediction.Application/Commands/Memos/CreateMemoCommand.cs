using EventFlow.Commands;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Commands.Memos;

public sealed class CreateMemoCommand : Command<MemoAggregate, MemoId>
{
    public CreateMemoCommand(MemoId aggregateId, string? authorId, string memoType,
        string content, DateTimeOffset createdAt, IReadOnlyList<MemoSubject> subjects,
        IReadOnlyList<MemoLink>? links = null)
        : base(aggregateId)
    {
        AuthorId = authorId;
        MemoType = memoType;
        Content = content;
        CreatedAt = createdAt;
        Subjects = subjects;
        Links = links ?? Array.Empty<MemoLink>();
    }

    public string? AuthorId { get; }
    public string MemoType { get; }
    public string Content { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<MemoSubject> Subjects { get; }
    public IReadOnlyList<MemoLink> Links { get; }
}
