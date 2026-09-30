using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRequestBulkRequest(
    [property: Required, StringLength(128, MinimumLength = 1)] string BatchId,
    [property: Required, MinLength(1), MaxLength(500)] IReadOnlyList<CollectionRequestBulkItemDto> Items);
