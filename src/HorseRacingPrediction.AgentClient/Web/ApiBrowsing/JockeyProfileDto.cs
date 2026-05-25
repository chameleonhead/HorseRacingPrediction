namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record JockeyProfileDto(
    string JockeyId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    IReadOnlyList<AliasDto> Aliases);
