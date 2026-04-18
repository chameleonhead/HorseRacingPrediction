namespace HorseRacingPrediction.Domain.Races;

public sealed record WeatherObservationDetails(
    DateTimeOffset ObservationTime,
    string? WeatherCode,
    string? WeatherText,
    decimal? TemperatureCelsius,
    decimal? HumidityPercent,
    string? WindDirectionCode,
    decimal? WindSpeedMeterPerSecond);
