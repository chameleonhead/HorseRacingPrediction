using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRequestBulkItemDto(
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
