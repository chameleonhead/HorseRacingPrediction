namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRequestSummaryDto(Guid RequestId, int RequestedRevision, CollectionReason Reason,
    DateTimeOffset RequestedAt, string? ExplicitUrl, string? BatchId);
