using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRequestBulkResponse(IReadOnlyList<CollectionRequestBulkOutcomeDto> Outcomes);
