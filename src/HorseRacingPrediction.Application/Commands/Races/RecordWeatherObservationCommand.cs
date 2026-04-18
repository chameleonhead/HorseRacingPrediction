using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RecordWeatherObservationCommand : Command<RaceAggregate, RaceId>
{
    public RecordWeatherObservationCommand(RaceId aggregateId, DateTimeOffset observationTime,
        string? weatherCode = null, string? weatherText = null,
        decimal? temperatureCelsius = null, decimal? humidityPercent = null,
        string? windDirectionCode = null, decimal? windSpeedMeterPerSecond = null)
        : base(aggregateId)
    {
        ObservationTime = observationTime;
        WeatherCode = weatherCode;
        WeatherText = weatherText;
        TemperatureCelsius = temperatureCelsius;
        HumidityPercent = humidityPercent;
        WindDirectionCode = windDirectionCode;
        WindSpeedMeterPerSecond = windSpeedMeterPerSecond;
    }

    public DateTimeOffset ObservationTime { get; }
    public string? WeatherCode { get; }
    public string? WeatherText { get; }
    public decimal? TemperatureCelsius { get; }
    public decimal? HumidityPercent { get; }
    public string? WindDirectionCode { get; }
    public decimal? WindSpeedMeterPerSecond { get; }
}
