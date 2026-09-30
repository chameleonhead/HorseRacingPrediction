
namespace HorseRacingPrediction.Contracts.Races;

public sealed record UpdateEntryCollectedDataRequest(
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? OwnerName);
