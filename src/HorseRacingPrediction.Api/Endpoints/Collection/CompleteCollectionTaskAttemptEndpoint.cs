using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CompleteCollectionTaskAttemptEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/collection/tasks/{id:guid}/attempts", async (Guid id, CompleteCollectionTaskAttemptRequest request,
            CollectionPlatformStore store, [FromServices] ICollectionDispatchTelemetry telemetry,
            CancellationToken token) =>
        {
            if (request.Attempt is null)
                return Results.BadRequest(new { message = "Attempt input is required." });
            var accepted = await store.CompleteAttemptAsync(id, request.Attempt.LeaseToken, JstTime.Now(),
                CollectionContractMapper.ToInternal(request.Attempt), token);
            if (accepted)
            {
                await telemetry.RecordTerminalCompletionLookupAsync(ct => store.GetTaskTelemetryStateAsync(id, ct), token)
                    .ConfigureAwait(false);
            }
            return accepted ? Results.NoContent() : Results.Conflict();
        });
}
