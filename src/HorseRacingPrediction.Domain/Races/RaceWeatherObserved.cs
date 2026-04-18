using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceWeatherObserved : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceWeatherObserved(DateTimeOffset observationTime,
        string? weatherCode = null, string? weatherText = null,
        decimal? temperatureCelsius = null, decimal? humidityPercent = null,
        string? windDirectionCode = null, decimal? windSpeedMeterPerSecond = null)
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
