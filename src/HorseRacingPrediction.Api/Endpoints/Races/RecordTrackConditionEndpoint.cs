using EventFlow;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class RecordTrackConditionEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/track-condition",
                    [SwaggerOperation(Summary = "Record track condition", Description = "Records a track condition observation for a race")]
        async (string raceId, HorseRacingPrediction.Contracts.Races.RecordTrackConditionRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var observation = request?.Observation;
                        if (observation is null) return Results.BadRequest(new[] { "Observation is required." });
                        try
                        {
                            var command = new RecordTrackConditionObservationCommand(
                                new RaceId(raceId),
                                observation.ObservationTime,
                                observation.TurfConditionCode,
                                observation.DirtConditionCode,
                                observation.GoingDescriptionText);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Ok()
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex)
                        {
                            // 天候記録と同じ理由（"Race is not created."）で409を返す。
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("RecordTrackCondition")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
