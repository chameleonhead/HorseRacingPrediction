
namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record TrainerSummaryDto(
    string TrainerId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    int AliasCount);
