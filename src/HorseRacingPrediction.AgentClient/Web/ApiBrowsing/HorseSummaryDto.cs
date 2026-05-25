namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record HorseSummaryDto(
    string HorseId,
    string RegisteredName,
    string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    int AliasCount);
