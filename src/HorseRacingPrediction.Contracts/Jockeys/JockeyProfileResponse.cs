
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record JockeyProfileResponse(
    string JockeyId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    IReadOnlyList<AliasDto> Aliases);
