
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record ApplyHorseIdentityRepairRequest(IReadOnlyList<string> CandidateIds);
