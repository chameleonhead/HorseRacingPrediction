using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Common.Time;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class AcquireNextExecutionEndpoint
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/collection/execution-leases", async (AcquireNextExecutionRequest request,
            CollectionPlatformStore store, IOptions<CollectionQueueOptions> queueOptions,
            [FromServices] ICollectionDispatchTelemetry telemetry, CancellationToken token) =>
        {
            if (request.Acquisition is null)
                return Results.BadRequest(new { message = "The execution acquisition is invalid." });
            var input = request.Acquisition;
            if (input.Wake is null)
                return Results.BadRequest(new { message = "The execution wake signal is invalid." });
            var wake = new CollectionWakeSignal(input.Wake.WakeId,
                input.Wake.DispatchEnvelopeId, input.Wake.ReservationToken, input.Wake.ContractVersion);
            var now = JstTime.Now();
            var result = await store.AcquireNextExecutionAsync(wake, input.QueueMessageId, now,
                TimeSpan.FromSeconds(45), token,
                aggregationDelayMilliseconds: queueOptions.Value.AggregationDelayMilliseconds);
            CollectionReservationReleaseOutcome? releaseOutcome = null;
            if (result.Status == CollectionExecutionAcquireStatus.Acquired)
            {
                var key = result.Envelope?.Compatibility;
                await telemetry.RecordAcquireAsync(result.Status, result.NoWorkReason, key?.Lane,
                    key?.Definition.Value, token).ConfigureAwait(false);
                return Results.Json(new AcquireNextExecutionResponse(CollectionContractMapper.ToDto(result)),
                    ResponseJsonOptions, statusCode: StatusCodes.Status201Created);
            }

            if (result.SafeToReleaseReservation)
            {
                var release = await store.ReleaseDispatchReservationAsync(wake.WakeId,
                    wake.DispatchEnvelopeId, wake.ReservationToken, now, token);
                releaseOutcome = release;
                result = result with { ReservationReleaseOutcome = release };
            }
            await telemetry.RecordAcquireAsync(result.Status, result.NoWorkReason, cancellationToken: token)
                .ConfigureAwait(false);
            if (releaseOutcome.HasValue)
                await telemetry.RecordReservationReleaseAsync(releaseOutcome.Value, token).ConfigureAwait(false);

            return Results.Json(new AcquireNextExecutionResponse(CollectionContractMapper.ToDto(result)),
                ResponseJsonOptions, statusCode: StatusCodes.Status200OK);
        })
        .Produces<AcquireNextExecutionResponse>(StatusCodes.Status200OK)
        .Produces<AcquireNextExecutionResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);
}
