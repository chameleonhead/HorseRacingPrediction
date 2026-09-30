
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectNameNormalizationPageDto(
    IReadOnlyList<SubjectNameNormalizationCandidateDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
