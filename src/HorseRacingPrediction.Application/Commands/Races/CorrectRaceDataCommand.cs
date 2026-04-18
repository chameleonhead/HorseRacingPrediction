using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class CorrectRaceDataCommand : Command<RaceAggregate, RaceId>
{
    public CorrectRaceDataCommand(RaceId aggregateId, string? raceName = null, string? racecourseCode = null,
        int? raceNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null,
        string? directionCode = null, string? reason = null)
        : base(aggregateId)
    {
        RaceName = raceName;
        RacecourseCode = racecourseCode;
        RaceNumber = raceNumber;
        GradeCode = gradeCode;
        SurfaceCode = surfaceCode;
        DistanceMeters = distanceMeters;
        DirectionCode = directionCode;
        Reason = reason;
    }

    public string? RaceName { get; }
    public string? RacecourseCode { get; }
    public int? RaceNumber { get; }
    public string? GradeCode { get; }
    public string? SurfaceCode { get; }
    public int? DistanceMeters { get; }
    public string? DirectionCode { get; }
    public string? Reason { get; }
}
