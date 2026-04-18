using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceResultDeclared : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceResultDeclared(string winningHorseName, DateTimeOffset declaredAt,
        string? winningHorseId = null, string? stewardReportText = null)
    {
        WinningHorseName = winningHorseName;
        DeclaredAt = declaredAt;
        WinningHorseId = winningHorseId;
        StewardReportText = stewardReportText;
    }

    public string WinningHorseName { get; }
    public DateTimeOffset DeclaredAt { get; }
    public string? WinningHorseId { get; }
    public string? StewardReportText { get; }
}
