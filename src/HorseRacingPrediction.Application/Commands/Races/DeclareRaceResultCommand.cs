using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class DeclareRaceResultCommand : Command<RaceAggregate, RaceId>
{
    public DeclareRaceResultCommand(RaceId aggregateId, string winningHorseName, DateTimeOffset declaredAt,
        string? winningHorseId = null, string? stewardReportText = null)
        : base(aggregateId)
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
