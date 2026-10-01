namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionFailureRecoveryResultDto(int SelectedCount, int CreatedTaskCount,
    int ReusedTaskCount, IReadOnlyList<Guid> TaskIds);
