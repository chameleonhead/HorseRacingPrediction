using EventFlow.Commands;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Commands.Jockeys;

public sealed class RegisterJockeyCommandHandler : CommandHandler<JockeyAggregate, JockeyId, RegisterJockeyCommand>
{
    public override Task ExecuteAsync(JockeyAggregate aggregate, RegisterJockeyCommand command, CancellationToken cancellationToken)
    {
        aggregate.RegisterJockey(command.DisplayName, command.NormalizedName, command.AffiliationCode);
        return Task.CompletedTask;
    }
}
