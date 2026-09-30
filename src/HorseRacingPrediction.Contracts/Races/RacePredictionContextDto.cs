
namespace HorseRacingPrediction.Contracts.Races;

public sealed class RacePredictionContextDto
{
    public string RaceId { get; set; } = string.Empty;
    public string? EntryAssignmentFingerprint { get; set; }
    public DateOnly? RaceDate { get; set; }
    public string? RacecourseCode { get; set; }
    public int? RaceNumber { get; set; }
    public string? RaceName { get; set; }
    public RaceStatus Status { get; set; }
    public string? GradeCode { get; set; }
    public string? SurfaceCode { get; set; }
    public int? DistanceMeters { get; set; }
    public string? DirectionCode { get; set; }
    public List<RacePredictionContextEntryDto> Entries { get; set; } = [];
    public List<RaceWeatherObservationDto> WeatherObservations { get; set; } = [];
    public List<RaceTrackConditionDto> TrackConditionObservations { get; set; } = [];

    public RaceWeatherObservationDto? LatestWeather => WeatherObservations.Count == 0
        ? null
        : WeatherObservations.OrderByDescending(x => x.ObservationTime).First();

    public RaceTrackConditionDto? LatestTrackCondition => TrackConditionObservations.Count == 0
        ? null
        : TrackConditionObservations.OrderByDescending(x => x.ObservationTime).First();
}
