namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectIdentificationExecutionDto(
    int SelectedCount, int CreatedTaskCount, int ReusedTaskCount, IReadOnlyList<Guid> TaskIds,
    int MergedCount = 0, int DisabledCollectionTaskCount = 0, int RunningCancellationRequestCount = 0);
