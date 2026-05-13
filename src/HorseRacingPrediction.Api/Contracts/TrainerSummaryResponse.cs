namespace HorseRacingPrediction.Api.Contracts;

public sealed record TrainerSummaryResponse(
    string TrainerId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    int AliasCount);