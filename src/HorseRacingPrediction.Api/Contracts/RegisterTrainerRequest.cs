using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record RegisterTrainerRequest(
    [property: Required, StringLength(128, MinimumLength = 1)] string DisplayName,
    [property: Required, StringLength(128, MinimumLength = 1)] string NormalizedName,
    string? AffiliationCode,
    string? TrainerId = null);
