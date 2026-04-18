using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class UpdateHorseMemoCommand : Command<HorseAggregate, HorseId>
{
    public UpdateHorseMemoCommand(HorseId aggregateId, string memoId,
        string? memoType = null, string? content = null,
        IReadOnlyList<HorseMemoLink>? links = null)
        : base(aggregateId)
    {
        MemoId = memoId;
        MemoType = memoType;
        Content = content;
        Links = links;
    }

    public string MemoId { get; }
    public string? MemoType { get; }
    public string? Content { get; }
    public IReadOnlyList<HorseMemoLink>? Links { get; }
}
