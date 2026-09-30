namespace HorseRacingPrediction.Contracts;

public sealed record RaceWeatherObservationDto(
    DateTimeOffset ObservationTime,
    string? WeatherCode,
    string? WeatherText,
    decimal? TemperatureCelsius,
    decimal? HumidityPercent,
    string? WindDirectionCode,
    decimal? WindSpeedMeterPerSecond);
