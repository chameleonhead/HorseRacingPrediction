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
        "/api/v2/internal/collection/execution-leases", async (CollectionExecutionAcquireRequest request,
            CollectionPlatformStore store, IOptions<CollectionQueueOptions> queueOptions,
            [FromServices] ICollectionDispatchTelemetry telemetry, CancellationToken token) =>
        {
            var now = JstTime.Now();
            var result = await store.AcquireNextExecutionAsync(request.Wake, request.QueueMessageId, now,
                TimeSpan.FromSeconds(45), token,
                aggregationDelayMilliseconds: queueOptions.Value.AggregationDelayMilliseconds);
            CollectionReservationReleaseOutcome? releaseOutcome = null;
            if (result.Status == CollectionExecutionAcquireStatus.Acquired)
            {
                var key = result.Envelope?.Compatibility;
                await telemetry.RecordAcquireAsync(result.Status, result.NoWorkReason, key?.Lane,
                    key?.Definition.Value, token).ConfigureAwait(false);
                return Results.Json(result, ResponseJsonOptions, statusCode: StatusCodes.Status201Created);
            }

            if (result.SafeToReleaseReservation)
            {
                var release = await store.ReleaseDispatchReservationAsync(request.Wake.WakeId,
                    request.Wake.DispatchEnvelopeId, request.Wake.ReservationToken, now, token);
                releaseOutcome = release;
                result = result with { ReservationReleaseOutcome = release };
            }
            await telemetry.RecordAcquireAsync(result.Status, result.NoWorkReason, cancellationToken: token)
                .ConfigureAwait(false);
            if (releaseOutcome.HasValue)
                await telemetry.RecordReservationReleaseAsync(releaseOutcome.Value, token).ConfigureAwait(false);

            return Results.Json(result, ResponseJsonOptions, statusCode: StatusCodes.Status200OK);
        });
}
