
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed class JockeyDto
{
    public string JockeyId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? AffiliationCode { get; set; }
    public List<AliasDto> Aliases { get; set; } = [];
}
