namespace HorseRacingPrediction.Contracts.Collection;

public sealed record SetCollectionPipelineInputDto(bool Paused, string? Reason = null);
