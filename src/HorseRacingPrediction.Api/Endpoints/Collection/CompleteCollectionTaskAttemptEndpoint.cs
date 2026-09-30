using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CompleteCollectionTaskAttemptEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/collection/tasks/{id:guid}/attempts", async (Guid id, CompleteCollectionAttemptRequest request,
            CollectionPlatformStore store, [FromServices] ICollectionDispatchTelemetry telemetry,
            CancellationToken token) =>
        {
            Uri.TryCreate(request.RequestedUrl, UriKind.Absolute, out var requestedUrl);
            Uri.TryCreate(request.FinalUrl, UriKind.Absolute, out var finalUrl);
            var accepted = await store.CompleteAttemptAsync(id, request.LeaseToken, JstTime.Now(), new(request.Result,
                request.ErrorCode, request.ErrorMessage, requestedUrl, finalUrl, request.HttpStatusCode,
                request.PageIdentification, request.RetryAt, request.NextCollectionAt, request.LocationOutcomes,
                request.FailureImpact, request.StageOutcomes, request.RaceEvidence), token);
            if (accepted)
            {
                await telemetry.RecordTerminalCompletionLookupAsync(ct => store.GetTaskTelemetryStateAsync(id, ct), token)
                    .ConfigureAwait(false);
            }
            return accepted ? Results.NoContent() : Results.Conflict();
        });
}
