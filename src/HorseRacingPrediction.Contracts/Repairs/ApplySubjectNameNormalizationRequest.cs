
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record ApplySubjectNameNormalizationRequest(
    IReadOnlyList<SubjectNameNormalizationInputDto> Items);
