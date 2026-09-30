
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record HorseIdentityRepairPreviewDto(
    string RepairId, IReadOnlyList<HorseIdentityRepairCandidateDto> Candidates);
