using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class TransitionCollectionExecutionEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPatch(
        "/api/v2/internal/collection/execution-batches/{id:guid}", async (Guid id, TransitionCollectionExecutionRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (request.Transition is null)
                return Results.BadRequest(new { message = "Transition input is required." });
            var input = request.Transition;
            if (input.Transition == "Start")
            {
                if (input.LeaseSeconds is null)
                    return Results.BadRequest(new { message = "Start requires leaseSeconds." });
                return await store.StartExecutionAsync(id, new(input.LeaseToken, input.LeaseSeconds.Value, input.LambdaRequestId), JstTime.Now(), token)
                    ? Results.NoContent() : Results.Conflict();
            }
            if (input.Transition == "Complete")
            {
                if (input.LeaseSeconds is not null || input.LambdaRequestId is not null)
                    return Results.BadRequest(new { message = "Complete accepts only the lease token." });
                return await store.CompleteExecutionAsync(id, input.LeaseToken, JstTime.Now(), token)
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
