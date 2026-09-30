using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateCollectionTaskEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v2/admin/collection/tasks",
            async (CollectionTaskRequest request, CollectionPlatformStore store, CancellationToken token) =>
            {
                if (string.Equals(request.Mode, "Resource", StringComparison.Ordinal))
                {
                    if (request.Resource is null || request.SourceUrl is not null)
                        return Results.BadRequest(new { message = "Resource mode accepts only resource." });
                    var resourceRequest = request.Resource;
                    if (!CollectionHttpUrl.TryCreate(resourceRequest.ExplicitUrl, out var explicitUrl)
                        && resourceRequest.ExplicitUrl is not null)
                        return Results.BadRequest(new { message = "ExplicitUrl must be an absolute HTTP(S) URL." });
                    try
                    {
                        var receipt = await store.RequestAsync(
                            new(resourceRequest.ResourceType, resourceRequest.Provider, resourceRequest.ResourceId),
                            new(resourceRequest.DefinitionId), resourceRequest.RequestedRevision, resourceRequest.Reason,
                            HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(), resourceRequest.Lane,
                            resourceRequest.Priority, explicitUrl, resourceRequest.BatchId,
                            resourceRequest.EffectiveDate, resourceRequest.Attributes, token);
                        var response = new CollectionTaskSubmissionResponse(request.Mode, receipt,
                            new(resourceRequest.ResourceType, resourceRequest.Provider, resourceRequest.ResourceId),
                            new(resourceRequest.DefinitionId), resourceRequest.EffectiveDate,
                            explicitUrl?.AbsoluteUri, resourceRequest.Attributes ?? new Dictionary<string, string>());
                        return receipt.CreatedTask
                            ? (IResult)Results.Accepted(TaskLocation(receipt.TaskId), response)
                            : Results.Ok(response);
                    }
                    catch (CollectionResourceSuppressedException)
                    {
                        return Results.Conflict(new { message = "補正済みのため収集対象外です。" });
                    }
                }

                if (string.Equals(request.Mode, "SourceUrl", StringComparison.Ordinal))
                {
                    if (request.SourceUrl is null || request.Resource is not null)
                        return Results.BadRequest(new { message = "SourceUrl mode accepts only sourceUrl." });
                    var identified = JraExplicitUrlResolver.Resolve(request.SourceUrl.Url);
                    if (!identified.Identified || identified.Resource is null || identified.Definition is null
                        || identified.EffectiveDate is null || identified.ExplicitUrl is null)
                        return Results.Json(identified, statusCode: StatusCodes.Status422UnprocessableEntity);
                    var explicitUrl = new Uri(identified.ExplicitUrl);
                    var resource = identified.Resource.Value;
                    var definition = identified.Definition.Value;
                    var lane = CollectionLane.Realtime;
                    var priority = resource.Type switch
                    {
                        HorseRacingPrediction.Contracts.Collection.CollectionResourceType.RaceOdds => (int)CollectionPriority.High,
                        HorseRacingPrediction.Contracts.Collection.CollectionResourceType.RaceResult => (int)CollectionPriority.Critical,
                        HorseRacingPrediction.Contracts.Collection.CollectionResourceType.RaceCard => 80,
                        _ => (int)CollectionPriority.Normal,
                    };
                    var currentRevision = await store.GetCurrentRevisionAsync(definition, token);
                    try
                    {
                        var receipt = await store.RequestAsync(resource, definition, currentRevision,
                            CollectionReason.ManualRefresh, HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(),
                            lane, priority, explicitUrl, effectiveDate: identified.EffectiveDate,
                            attributes: identified.Attributes, cancellationToken: token);
                        var body = new CollectionTaskSubmissionResponse(request.Mode, receipt, resource,
                            definition, identified.EffectiveDate, explicitUrl.AbsoluteUri, identified.Attributes);
                        return receipt.CreatedTask
                            ? (IResult)Results.Accepted(TaskLocation(receipt.TaskId), body)
                            : Results.Ok(body);
                    }
                    catch (CollectionResourceSuppressedException)
                    {
                        return Results.Conflict(new { message = "補正済みのため収集対象外です。" });
                    }
                }
                return Results.BadRequest(new { message = "mode must be Resource or SourceUrl." });
            })
            .WithName("CreateCollectionTask")
            .WithTags("Collection Platform")
            .Produces<CollectionTaskSubmissionResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    private static string? TaskLocation(Guid? taskId) => taskId is { } id
        ? $"/api/v2/admin/collection/tasks/{id:D}" : null;
}
