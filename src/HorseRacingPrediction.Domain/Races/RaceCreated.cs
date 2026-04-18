using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceCreated : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceCreated(DateOnly raceDate, string racecourseCode, int raceNumber, string raceName,
        int? meetingNumber = null, int? dayNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null, string? directionCode = null)
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
