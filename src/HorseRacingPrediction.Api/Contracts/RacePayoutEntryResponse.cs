namespace HorseRacingPrediction.Api.Contracts;

public sealed record RacePayoutEntryResponse(
    string Combination,
    decimal Amount);
