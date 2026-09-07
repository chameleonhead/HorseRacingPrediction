namespace HorseRacingPrediction.Api.Contracts;

public sealed record UpdateEntryCollectedDataRequest(
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? OwnerName);
