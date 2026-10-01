namespace HorseRacingPrediction.Contracts.Collection;

public sealed record TransitionCollectionExecutionInputDto(string Transition, string LeaseToken,
    int? LeaseSeconds = null, string? LambdaRequestId = null);
