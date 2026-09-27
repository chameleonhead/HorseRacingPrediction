using EventFlow;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class RecordWeatherObservationEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/weather",
                    [SwaggerOperation(Summary = "Record weather observation", Description = "Records a weather observation for a race")]
        async (string raceId, ApiContracts.RecordWeatherObservationRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var command = new RecordWeatherObservationCommand(
                                new RaceId(raceId),
                                request.ObservationTime,
                                request.WeatherCode,
                                request.WeatherText,
                                request.TemperatureCelsius,
                                request.HumidityPercent,
                                request.WindDirectionCode,
                                request.WindSpeedMeterPerSecond);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Ok()
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex)
                        {
                            // レース結果収集はレース作成日程（RaceCardCollectionの前方参照ウィンドウ）と
                            // 独立して過去日を遡って動作しうるため、出馬表収集が一度も行われず
                            // RaceAggregateがCreateされていないレースに対して天候記録が呼ばれることがある
                            // （"Race is not created." のドメイン不変条件違反）。これは未処理例外として
                            // 500を返すべきではなく、他のレースAPI（DeclareRaceResult等）と同様に
                            // 409で明示的に応答する。
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("RecordWeatherObservation")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
