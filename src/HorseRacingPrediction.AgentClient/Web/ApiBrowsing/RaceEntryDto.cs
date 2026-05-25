namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record RaceEntryDto(
    string EntryId,
    string HorseId,
    string? HorseName,
    int HorseNumber,
    string? JockeyId,
    string? TrainerId,
    int? GateNumber,
    decimal? AssignedWeight,
    string? SexCode,
    int? Age,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? RunningStyleCode);
