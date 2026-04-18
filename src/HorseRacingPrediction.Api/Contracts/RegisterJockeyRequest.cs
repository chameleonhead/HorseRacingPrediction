using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record RegisterJockeyRequest(
    [property: Required, StringLength(128, MinimumLength = 1)] string DisplayName,
    [property: Required, StringLength(128, MinimumLength = 1)] string NormalizedName,
    string? AffiliationCode);
