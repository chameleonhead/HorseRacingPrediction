using EventFlow.Commands;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Commands.Memos;

public sealed class ChangeMemoSubjectsCommand : Command<MemoAggregate, MemoId>
{
    public ChangeMemoSubjectsCommand(MemoId aggregateId, IReadOnlyList<MemoSubject> newSubjects)
        : base(aggregateId)
    {
        NewSubjects = newSubjects;
    }

    public IReadOnlyList<MemoSubject> NewSubjects { get; }
}
