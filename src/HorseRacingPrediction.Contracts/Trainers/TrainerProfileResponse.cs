
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record TrainerProfileResponse(
    string TrainerId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    IReadOnlyList<AliasDto> Aliases);
