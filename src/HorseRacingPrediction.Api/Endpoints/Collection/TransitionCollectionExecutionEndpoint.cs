using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class TransitionCollectionExecutionEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPatch(
        "/api/v2/internal/collection/execution-batches/{id:guid}", async (Guid id,
            CollectionExecutionTransitionRequest request, CollectionPlatformStore store, CancellationToken token) =>
        {
            if (request.Transition == "Start")
            {
                if (request.LeaseSeconds is null)
                    return Results.BadRequest(new { message = "Start requires leaseSeconds." });
                return await store.StartExecutionAsync(id, new(request.LeaseToken, request.LeaseSeconds.Value, request.LambdaRequestId), JstTime.Now(), token)
                    ? Results.NoContent() : Results.Conflict();
            }
            if (request.Transition == "Complete")
            {
                if (request.LeaseSeconds is not null || request.LambdaRequestId is not null)
                    return Results.BadRequest(new { message = "Complete accepts only the lease token." });
                return await store.CompleteExecutionAsync(id, request.LeaseToken, JstTime.Now(), token)
                    ? Results.NoContent() : Results.Conflict();
            }
            return Results.BadRequest(new { message = "Transition must be Start or Complete." });
        })
        .WithName("TransitionCollectionExecution")
        .WithTags("Collection Worker")
        .WithDescription("transition is Start or Complete. Start accepts leaseSeconds; Complete accepts only the lease token.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
}
internal sealed record CollectionExecutionTransitionRequest(string Transition, string LeaseToken,
    int? LeaseSeconds = null, string? LambdaRequestId = null);
