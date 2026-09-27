using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateCollectionTaskBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v2/admin/collection/task-batches",
            async (CollectionTaskBatchRequest request, CollectionPlatformStore store,
                IDbContextProvider<EventStoreDbContext> domain,
                IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
            {
                if (string.Equals(request.Mode, "PreviewSelection", StringComparison.Ordinal))
                {
                    if (request.PreviewSelection is null || request.ExplicitItems is not null)
                        return Results.BadRequest(new { message = "PreviewSelection mode accepts only previewSelection." });
                    if (!Enum.TryParse<BulkCollectionSelection>(request.PreviewSelection.Selection, true,
                            out var selection))
                        return Results.BadRequest(new { message = "Selection is invalid." });
                    var value = request.PreviewSelection;
                    var bulk = new BulkCollectionOperationRequest(value.DefinitionId, value.RequestedRevision,
                        value.Reason, selection, value.Provider, value.Resources, value.From, value.To,
                        value.TrainerId, value.LastCollectedBefore, null, value.ExpectedResources, value.BatchId,
                        value.Lane, value.Priority);
                    var targets = await CollectionPlatformEndpointSupport.ResolveBulkTargetsAsync(
                        bulk, store, domain, conditions, token);
                    var actual = targets.Select(x => x.Resource.Normalize()).Distinct()
                        .OrderBy(x => x.ToString()).ToArray();
                    var expected = (bulk.ExpectedResources ?? []).Select(x => x.Normalize()).Distinct()
                        .OrderBy(x => x.ToString()).ToArray();
                    if (expected.Length == 0 || !actual.SequenceEqual(expected))
                        return Results.Conflict(new { message = "Selection changed after preview; preview again before executing." });
                    var batchId = string.IsNullOrWhiteSpace(bulk.BatchId)
                        ? $"manual:{Guid.NewGuid():N}" : bulk.BatchId;
                    var result = await store.ExecuteBulkRequestAsync(new(bulk.DefinitionId),
                        bulk.RequestedRevision, bulk.Reason, targets,
                        HorseRacingPrediction.Contracts.Time.JstTime.Now(), batchId,
                        bulk.Lane, bulk.Priority, token);
                    return Results.Accepted(value: new CollectionTaskBatchSubmissionResponse(
                        request.Mode, result, null));
                }

                if (string.Equals(request.Mode, "ExplicitItems", StringComparison.Ordinal))
                {
                    if (request.ExplicitItems is null || request.PreviewSelection is not null)
                        return Results.BadRequest(new { message = "ExplicitItems mode accepts only explicitItems." });
                    var itemRequest = request.ExplicitItems;
                    if (itemRequest.Items is null || itemRequest.Items.Count is < 1 or > 500)
                        return Results.BadRequest(new { message = "Batch items must contain between 1 and 500 entries." });
                    if (itemRequest.Items.Select(item => item.ItemKey)
                            .Distinct(StringComparer.Ordinal).Count() != itemRequest.Items.Count)
                        return Results.BadRequest(new { message = "Batch item keys must be unique." });
                    var items = new List<CollectionRequestBatchItem>(itemRequest.Items.Count);
                    foreach (var item in itemRequest.Items)
                    {
                        if (!Enum.TryParse<CollectionResourceType>(item.ResourceType, true, out var resourceType)
                            || !Enum.TryParse<CollectionReason>(item.Reason, true, out var reason)
                            || !Enum.TryParse<CollectionLane>(item.Lane, true, out var lane))
                            return Results.BadRequest(new { message = $"Invalid enum value for item {item.ItemKey}." });
                        if (!CollectionHttpUrl.TryCreate(item.ExplicitUrl, out var explicitUrl)
                            && item.ExplicitUrl is not null)
                            return Results.BadRequest(new { message = $"ExplicitUrl is invalid for item {item.ItemKey}." });
                        items.Add(new(item.ItemKey, new(resourceType, item.Provider, item.ResourceId),
                            new(item.DefinitionId), item.RequestedRevision, reason, lane, item.Priority,
                            explicitUrl, item.EffectiveDate, item.Attributes));
                    }
                    var outcomes = await store.RequestManyAsync(itemRequest.BatchId, items,
                        HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
                    var itemResponse = new CollectionRequestBulkResponse(outcomes.Select(outcome =>
                        new CollectionRequestBulkOutcome(outcome.ItemKey, outcome.Status,
                            outcome.Receipt?.RequestId, outcome.Receipt?.TaskId,
                            outcome.Receipt?.CreatedTask ?? false, outcome.ErrorCode, outcome.Message)).ToArray());
                    var response = new CollectionTaskBatchSubmissionResponse(request.Mode, null, itemResponse);
                    return outcomes.All(x => string.Equals(x.Status, "Accepted", StringComparison.OrdinalIgnoreCase))
                        ? Results.Accepted(value: response)
                        : Results.Json(response, statusCode: StatusCodes.Status207MultiStatus);
                }
                return Results.BadRequest(new { message = "mode must be PreviewSelection or ExplicitItems." });
            })
            .WithName("CreateCollectionTaskBatch")
            .WithTags("Collection Platform")
            .Produces<CollectionTaskBatchSubmissionResponse>(StatusCodes.Status200OK)
            .Produces<CollectionTaskBatchSubmissionResponse>(StatusCodes.Status202Accepted)
            .Produces<CollectionTaskBatchSubmissionResponse>(StatusCodes.Status207MultiStatus)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);
}
