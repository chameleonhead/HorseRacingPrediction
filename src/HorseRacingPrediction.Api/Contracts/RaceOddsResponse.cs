namespace HorseRacingPrediction.Api.Contracts;

public sealed record RaceOddsResponse(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<RaceWinOddsResponse> WinOdds,
    IReadOnlyList<RacePlaceOddsResponse> PlaceOdds);
