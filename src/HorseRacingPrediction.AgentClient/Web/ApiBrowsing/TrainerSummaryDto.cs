namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record TrainerSummaryDto(
    string TrainerId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    int AliasCount);
