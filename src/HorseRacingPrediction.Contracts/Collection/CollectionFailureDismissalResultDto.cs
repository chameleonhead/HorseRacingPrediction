namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionFailureDismissalResultDto(int SelectedCount, int DismissedCount,
    int AlreadyClosedCount, bool HasRecoveryConflict = false);
