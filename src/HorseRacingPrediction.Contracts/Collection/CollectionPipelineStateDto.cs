namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionPipelineStateDto(bool IsPaused, string? Reason, DateTimeOffset? UpdatedAt);
