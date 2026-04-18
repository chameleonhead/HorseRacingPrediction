namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record WeatherObservationSnapshot(
    DateTimeOffset ObservationTime,
    string? WeatherCode,
    string? WeatherText,
    decimal? TemperatureCelsius,
    decimal? HumidityPercent,
    string? WindDirectionCode,
    decimal? WindSpeedMeterPerSecond);
