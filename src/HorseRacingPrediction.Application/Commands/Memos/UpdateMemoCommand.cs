using EventFlow.Commands;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Commands.Memos;

public sealed class UpdateMemoCommand : Command<MemoAggregate, MemoId>
{
    public UpdateMemoCommand(MemoId aggregateId, string? memoType = null,
        string? content = null, IReadOnlyList<MemoLink>? links = null)
        : base(aggregateId)
    {
        MemoType = memoType;
        Content = content;
        Links = links;
    }

    public string? MemoType { get; }
    public string? Content { get; }
    public IReadOnlyList<MemoLink>? Links { get; }
}
