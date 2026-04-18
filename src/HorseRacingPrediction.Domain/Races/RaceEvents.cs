using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceCreated : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceCreated(DateOnly raceDate, string racecourseCode, int raceNumber, string raceName)
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

public sealed class RaceCardPublished : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceCardPublished(int entryCount)
    {
        EntryCount = entryCount;
    }

    public int EntryCount { get; }
}

public sealed class RaceResultDeclared : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceResultDeclared(string winningHorseName, DateTimeOffset declaredAt)
    {
        WinningHorseName = winningHorseName;
        DeclaredAt = declaredAt;
    }

    public string WinningHorseName { get; }
    public DateTimeOffset DeclaredAt { get; }
}
