namespace HorseRacingPrediction.Contracts.Races;

public sealed record UpdateEntryCollectedDataInputDto(
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? OwnerName);
