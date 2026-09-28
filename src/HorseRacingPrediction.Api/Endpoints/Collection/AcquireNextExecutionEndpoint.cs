using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Time;
using Microsoft.Extensions.Options;
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
            CollectionPlatformStore store, IOptions<CollectionQueueOptions> queueOptions, CancellationToken token) =>
        {
            var now = JstTime.Now();
            var result = await store.AcquireNextExecutionAsync(request.Wake, request.QueueMessageId, now,
                TimeSpan.FromSeconds(45), token,
                aggregationDelayMilliseconds: queueOptions.Value.AggregationDelayMilliseconds);
            if (result.Status == CollectionExecutionAcquireStatus.Acquired)
                return Results.Json(result, ResponseJsonOptions, statusCode: StatusCodes.Status201Created);

            if (result.SafeToReleaseReservation)
            {
                var release = await store.ReleaseDispatchReservationAsync(request.Wake.WakeId,
                    request.Wake.DispatchEnvelopeId, request.Wake.ReservationToken, now, token);
                result = result with { ReservationReleaseOutcome = release };
            }

            return Results.Json(result, ResponseJsonOptions, statusCode: StatusCodes.Status200OK);
        });
}
