using EventFlow.Commands;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Commands.Memos;

public sealed class ChangeMemoSubjectsCommandHandler : CommandHandler<MemoAggregate, MemoId, ChangeMemoSubjectsCommand>
{
    public override Task ExecuteAsync(MemoAggregate aggregate, ChangeMemoSubjectsCommand command, CancellationToken cancellationToken)
    {
        aggregate.ChangeSubjects(command.NewSubjects);
        return Task.CompletedTask;
    }
}
