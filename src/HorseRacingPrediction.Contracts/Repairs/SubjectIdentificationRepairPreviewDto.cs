
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectIdentificationRepairPreviewDto(
    IReadOnlyList<SubjectIdentificationRepairCandidateDto> Candidates);
