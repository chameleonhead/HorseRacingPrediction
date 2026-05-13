namespace HorseRacingPrediction.Agents.Contracts;

public sealed class RacePredictionContextReadModel
{
    public string RaceId { get; set; } = string.Empty;
    public DateOnly? RaceDate { get; set; }
    public string? RacecourseCode { get; set; }
    public int? RaceNumber { get; set; }
    public string? RaceName { get; set; }
    public RaceStatus Status { get; set; }
    public string? GradeCode { get; set; }
    public string? SurfaceCode { get; set; }
    public int? DistanceMeters { get; set; }
    public string? DirectionCode { get; set; }
    public List<RacePredictionContextEntry> Entries { get; set; } = [];
    public List<WeatherObservationSnapshot> WeatherObservations { get; set; } = [];
    public List<TrackConditionSnapshot> TrackConditionObservations { get; set; } = [];

    public WeatherObservationSnapshot? LatestWeather => WeatherObservations.Count == 0
        ? null
        : WeatherObservations.OrderByDescending(x => x.ObservationTime).First();

    public TrackConditionSnapshot? LatestTrackCondition => TrackConditionObservations.Count == 0
        ? null
        : TrackConditionObservations.OrderByDescending(x => x.ObservationTime).First();
}