using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class DeclareEntryResultCommandHandler : CommandHandler<RaceAggregate, RaceId, DeclareEntryResultCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, DeclareEntryResultCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeclareEntryResult(command.EntryId, command.FinishPosition, command.OfficialTime,
            command.MarginText, command.LastThreeFurlongTime, command.AbnormalResultCode, command.PrizeMoney);
        return Task.CompletedTask;
    }
}
