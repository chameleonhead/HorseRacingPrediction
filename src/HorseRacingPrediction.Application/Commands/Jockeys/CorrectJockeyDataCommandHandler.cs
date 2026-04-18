using EventFlow.Commands;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Commands.Jockeys;

public sealed class CorrectJockeyDataCommandHandler : CommandHandler<JockeyAggregate, JockeyId, CorrectJockeyDataCommand>
{
    public override Task ExecuteAsync(JockeyAggregate aggregate, CorrectJockeyDataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectData(command.DisplayName, command.NormalizedName, command.AffiliationCode, command.Reason);
        return Task.CompletedTask;
    }
}
