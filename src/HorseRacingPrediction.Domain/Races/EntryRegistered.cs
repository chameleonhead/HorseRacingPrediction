using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class EntryRegistered : AggregateEvent<RaceAggregate, RaceId>
{
    public EntryRegistered(string entryId, string horseId, int horseNumber,
        string? jockeyId = null, string? trainerId = null,
        int? gateNumber = null, decimal? assignedWeight = null,
        string? sexCode = null, int? age = null,
        decimal? declaredWeight = null, decimal? declaredWeightDiff = null,
        string? runningStyleCode = null,
        DateOnly? raceDate = null, string? racecourseCode = null,
        string? surfaceCode = null, int? distanceMeters = null,
        string? directionCode = null, string? gradeCode = null)
    {
        EntryId = entryId;
        HorseId = horseId;
        HorseNumber = horseNumber;
        JockeyId = jockeyId;
        TrainerId = trainerId;
        GateNumber = gateNumber;
        AssignedWeight = assignedWeight;
        SexCode = sexCode;
        Age = age;
        DeclaredWeight = declaredWeight;
        DeclaredWeightDiff = declaredWeightDiff;
        RunningStyleCode = runningStyleCode;
        RaceDate = raceDate;
        RacecourseCode = racecourseCode;
        SurfaceCode = surfaceCode;
        DistanceMeters = distanceMeters;
        DirectionCode = directionCode;
        GradeCode = gradeCode;
    }

    public string EntryId { get; }
    public string HorseId { get; }
    public int HorseNumber { get; }
    public string? JockeyId { get; }
    public string? TrainerId { get; }
    public int? GateNumber { get; }
    public decimal? AssignedWeight { get; }
    public string? SexCode { get; }
    public int? Age { get; }
    public decimal? DeclaredWeight { get; }
    public decimal? DeclaredWeightDiff { get; }
    public string? RunningStyleCode { get; }
    public DateOnly? RaceDate { get; }
    public string? RacecourseCode { get; }
    public string? SurfaceCode { get; }
    public int? DistanceMeters { get; }
    public string? DirectionCode { get; }
    public string? GradeCode { get; }
}
