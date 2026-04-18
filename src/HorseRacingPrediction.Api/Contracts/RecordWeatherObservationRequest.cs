using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record RecordWeatherObservationRequest(
    [property: Required] DateTimeOffset ObservationTime,
    string? WeatherCode,
    string? WeatherText,
    decimal? TemperatureCelsius,
    decimal? HumidityPercent,
    string? WindDirectionCode,
    decimal? WindSpeedMeterPerSecond);
