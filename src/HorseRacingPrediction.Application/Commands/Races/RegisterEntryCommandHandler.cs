using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RegisterEntryCommandHandler : CommandHandler<RaceAggregate, RaceId, RegisterEntryCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RegisterEntryCommand command, CancellationToken cancellationToken)
    {
        aggregate.RegisterEntry(command.EntryId, command.HorseId, command.HorseNumber,
            command.JockeyId, command.TrainerId, command.GateNumber, command.AssignedWeight,
            command.SexCode, command.Age, command.DeclaredWeight, command.DeclaredWeightDiff,
            command.RunningStyleCode);
        return Task.CompletedTask;
    }
}
