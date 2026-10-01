namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionDispatchTaskReferenceDto(Guid TaskId, long DispatchGeneration)
{
    public bool IsSupported() => TaskId != Guid.Empty && DispatchGeneration > 0;
}
