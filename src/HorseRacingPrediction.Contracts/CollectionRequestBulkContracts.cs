using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts;

public sealed record CollectionRequestBulkRequest(
    [property: Required, StringLength(128, MinimumLength = 1)] string BatchId,
    [property: Required, MinLength(1), MaxLength(500)] IReadOnlyList<CollectionRequestBulkItem> Items);

public sealed record CollectionRequestBulkItem(
    [property: Required, StringLength(128, MinimumLength = 1)] string ItemKey,
    [property: Required] string ResourceType,
    [property: Required, StringLength(32, MinimumLength = 1)] string Provider,
    [property: Required, StringLength(256, MinimumLength = 1)] string ResourceId,
    [property: Required, StringLength(128, MinimumLength = 1)] string DefinitionId,
    [property: Range(1, int.MaxValue)] int RequestedRevision,
    [property: Required] string Reason,
    [property: Required] string Lane,
    int Priority,
    string? ExplicitUrl = null,
    DateOnly? EffectiveDate = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record CollectionRequestBulkResponse(IReadOnlyList<CollectionRequestBulkOutcome> Outcomes);

public sealed record CollectionRequestBulkOutcome(string ItemKey, string Status,
    Guid? RequestId = null, Guid? TaskId = null, bool CreatedTask = false,
    string? ErrorCode = null, string? Message = null);
