using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record RacePredictionContextReadModel(
    string RaceId,
    DateOnly RaceDate,
    string RacecourseCode,
    int RaceNumber,
    string RaceName,
    RaceStatus Status,
    string? GradeCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    IReadOnlyList<RacePredictionContextEntry> Entries,
    WeatherObservationSnapshot? LatestWeather,
    TrackConditionSnapshot? LatestTrackCondition);
