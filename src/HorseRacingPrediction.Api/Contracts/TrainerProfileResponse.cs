namespace HorseRacingPrediction.Api.Contracts;

public sealed record TrainerProfileResponse(
    string TrainerId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    IReadOnlyList<AliasResponse> Aliases);
