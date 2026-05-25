namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record HorseProfileDto(
    string HorseId,
    string RegisteredName,
    string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    IReadOnlyList<AliasDto> Aliases);
