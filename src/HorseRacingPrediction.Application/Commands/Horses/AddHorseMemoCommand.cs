using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class AddHorseMemoCommand : Command<HorseAggregate, HorseId>
{
    public AddHorseMemoCommand(HorseId aggregateId, string memoId, string? authorId,
        string memoType, string content, DateTimeOffset createdAt,
        IReadOnlyList<HorseMemoLink>? links = null)
        : base(aggregateId)
    {
        MemoId = memoId;
        AuthorId = authorId;
        MemoType = memoType;
        Content = content;
        CreatedAt = createdAt;
        Links = links ?? Array.Empty<HorseMemoLink>();
    }

    public string MemoId { get; }
    public string? AuthorId { get; }
    public string MemoType { get; }
    public string Content { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<HorseMemoLink> Links { get; }
}
