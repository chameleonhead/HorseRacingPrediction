using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRequestBulkOutcomeDto(string ItemKey, string Status,
    Guid? RequestId = null, Guid? TaskId = null, bool CreatedTask = false,
    string? ErrorCode = null, string? Message = null);
