
namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record UpdateTrainerProfileRequest(
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode);
