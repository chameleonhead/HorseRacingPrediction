using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record RegisterJockeyInputDto(
    [property: Required, StringLength(128, MinimumLength = 1)] string DisplayName,
    [property: Required, StringLength(128, MinimumLength = 1)] string NormalizedName,
    string? AffiliationCode,
    string? JockeyId = null);
