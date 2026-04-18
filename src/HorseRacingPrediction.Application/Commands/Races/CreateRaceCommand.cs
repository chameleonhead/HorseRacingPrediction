using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class CreateRaceCommand : Command<RaceAggregate, RaceId>
{
    public CreateRaceCommand(RaceId aggregateId, DateOnly raceDate, string racecourseCode, int raceNumber, string raceName,
        int? meetingNumber = null, int? dayNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null, string? directionCode = null)
        : base(aggregateId)
    {
        RaceDate = raceDate;
        RacecourseCode = racecourseCode;
        RaceNumber = raceNumber;
        RaceName = raceName;
        MeetingNumber = meetingNumber;
        DayNumber = dayNumber;
        GradeCode = gradeCode;
        SurfaceCode = surfaceCode;
        DistanceMeters = distanceMeters;
        DirectionCode = directionCode;
    }

    public DateOnly RaceDate { get; }
    public string RacecourseCode { get; }
    public int RaceNumber { get; }
    public string RaceName { get; }
    public int? MeetingNumber { get; }
    public int? DayNumber { get; }
    public string? GradeCode { get; }
    public string? SurfaceCode { get; }
    public int? DistanceMeters { get; }
    public string? DirectionCode { get; }
}
