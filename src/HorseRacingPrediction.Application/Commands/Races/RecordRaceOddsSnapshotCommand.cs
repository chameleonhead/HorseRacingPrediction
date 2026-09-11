using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RecordRaceOddsSnapshotCommand(RaceId id, DateTimeOffset observedAt,
    IReadOnlyList<RaceOddsEntry> entries) : Command<RaceAggregate, RaceId>(id)
{
    public DateTimeOffset ObservedAt { get; } = observedAt;
    public IReadOnlyList<RaceOddsEntry> Entries { get; } = entries;
}

public sealed class RecordRaceOddsSnapshotCommandHandler
    : CommandHandler<RaceAggregate, RaceId, RecordRaceOddsSnapshotCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RecordRaceOddsSnapshotCommand command,
        CancellationToken cancellationToken)
    {
        aggregate.RecordOddsSnapshot(command.ObservedAt, command.Entries);
        return Task.CompletedTask;
    }
}
