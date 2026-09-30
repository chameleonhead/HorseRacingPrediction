
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record ExecuteSubjectIdentificationRepairRequest(
    IReadOnlyList<SubjectIdentificationRepairInputDto> Items);
