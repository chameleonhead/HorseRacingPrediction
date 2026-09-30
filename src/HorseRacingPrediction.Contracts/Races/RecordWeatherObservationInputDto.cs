using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record RecordWeatherObservationInputDto(
    [property: Required] DateTimeOffset ObservationTime,
    string? WeatherCode,
    string? WeatherText,
    decimal? TemperatureCelsius,
    decimal? HumidityPercent,
    string? WindDirectionCode,
    decimal? WindSpeedMeterPerSecond);
