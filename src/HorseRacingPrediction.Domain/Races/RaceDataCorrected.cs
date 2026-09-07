using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceDataCorrected : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceDataCorrected(string? raceName = null, string? racecourseCode = null,
        int? raceNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null,
        string? directionCode = null, string? reason = null, int? entryCount = null, TimeOnly? startTime = null, string? overallPaceText = null, string? cornerPassagesText = null, string? courseLayout = null)
    {
        RaceName = raceName;
        RacecourseCode = racecourseCode;
        RaceNumber = raceNumber;
        GradeCode = gradeCode;
        SurfaceCode = surfaceCode;
        DistanceMeters = distanceMeters;
        DirectionCode = directionCode;
        Reason = reason;
        EntryCount = entryCount;
        StartTime = startTime; OverallPaceText = overallPaceText; CornerPassagesText = cornerPassagesText; CourseLayout = courseLayout;
    }

    public TimeOnly? StartTime { get; }
    public string? OverallPaceText { get; }
    public string? CornerPassagesText { get; }
    public string? CourseLayout { get; }
    public int? EntryCount { get; }
    public string? RaceName { get; }
    public string? RacecourseCode { get; }
    public int? RaceNumber { get; }
    public string? GradeCode { get; }
    public string? SurfaceCode { get; }
    public int? DistanceMeters { get; }
    public string? DirectionCode { get; }
    public string? Reason { get; }
}
