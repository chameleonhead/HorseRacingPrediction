namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record TrainerProfileDto(
    string TrainerId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    IReadOnlyList<AliasDto> Aliases);
