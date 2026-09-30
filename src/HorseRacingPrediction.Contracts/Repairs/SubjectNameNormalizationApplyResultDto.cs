
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectNameNormalizationApplyResultDto(
    int SelectedCount,
    int AppliedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<SubjectNameNormalizationItemResultDto> Items);
