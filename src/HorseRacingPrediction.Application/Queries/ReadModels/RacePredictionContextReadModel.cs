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

public sealed record RacePredictionContextEntry(
    string EntryId,
    string HorseId,
    int HorseNumber,
    string? JockeyId,
    string? TrainerId,
    int? GateNumber,
    decimal? AssignedWeight);

public sealed record WeatherObservationSnapshot(
    DateTimeOffset ObservationTime,
    string? WeatherCode,
    string? WeatherText,
    decimal? TemperatureCelsius,
    decimal? HumidityPercent,
    string? WindDirectionCode,
    decimal? WindSpeedMeterPerSecond);

public sealed record TrackConditionSnapshot(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);
