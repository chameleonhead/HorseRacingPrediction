using EventFlow.Commands;

namespace HorseRacingPrediction.Domain.Races;

public sealed class CreateRaceCommand : Command<RaceAggregate, RaceId>
{
    public CreateRaceCommand(RaceId aggregateId, DateOnly raceDate, string racecourseCode, int raceNumber, string raceName)
        : base(aggregateId)
    {
        RaceDate = raceDate;
        RacecourseCode = racecourseCode;
        RaceNumber = raceNumber;
        RaceName = raceName;
    }

    public DateOnly RaceDate { get; }
    public string RacecourseCode { get; }
    public int RaceNumber { get; }
    public string RaceName { get; }
}

public sealed class CreateRaceCommandHandler : CommandHandler<RaceAggregate, RaceId, CreateRaceCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, CreateRaceCommand command, CancellationToken cancellationToken)
    {
        aggregate.Create(command.RaceDate, command.RacecourseCode, command.RaceNumber, command.RaceName);
        return Task.CompletedTask;
    }
}

public sealed class PublishRaceCardCommand : Command<RaceAggregate, RaceId>
{
    public PublishRaceCardCommand(RaceId aggregateId, int entryCount)
        : base(aggregateId)
    {
        EntryCount = entryCount;
    }

    public int EntryCount { get; }
}

public sealed class PublishRaceCardCommandHandler : CommandHandler<RaceAggregate, RaceId, PublishRaceCardCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, PublishRaceCardCommand command, CancellationToken cancellationToken)
    {
        aggregate.PublishCard(command.EntryCount);
        return Task.CompletedTask;
    }
}

public sealed class DeclareRaceResultCommand : Command<RaceAggregate, RaceId>
{
    public DeclareRaceResultCommand(RaceId aggregateId, string winningHorseName, DateTimeOffset declaredAt)
        : base(aggregateId)
    {
        WinningHorseName = winningHorseName;
        DeclaredAt = declaredAt;
    }

    public string WinningHorseName { get; }
    public DateTimeOffset DeclaredAt { get; }
}

public sealed class DeclareRaceResultCommandHandler : CommandHandler<RaceAggregate, RaceId, DeclareRaceResultCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, DeclareRaceResultCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeclareResult(command.WinningHorseName, command.DeclaredAt);
        return Task.CompletedTask;
    }
}
