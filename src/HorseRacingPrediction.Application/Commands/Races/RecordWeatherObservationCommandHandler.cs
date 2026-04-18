using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RecordWeatherObservationCommandHandler : CommandHandler<RaceAggregate, RaceId, RecordWeatherObservationCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RecordWeatherObservationCommand command, CancellationToken cancellationToken)
    {
        aggregate.RecordWeatherObservation(command.ObservationTime,
            command.WeatherCode, command.WeatherText, command.TemperatureCelsius, command.HumidityPercent,
            command.WindDirectionCode, command.WindSpeedMeterPerSecond);
        return Task.CompletedTask;
    }
}
