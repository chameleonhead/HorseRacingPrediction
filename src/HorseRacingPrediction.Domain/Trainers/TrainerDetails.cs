namespace HorseRacingPrediction.Domain.Trainers;

public sealed record TrainerDetails(
    string TrainerId,
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode,
    IReadOnlyCollection<AliasDetails> Aliases);
