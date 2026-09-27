namespace HorseRacingPrediction.Contracts;

public sealed record RacePayoutEntryResponse(
    string Combination,
    decimal Amount);
