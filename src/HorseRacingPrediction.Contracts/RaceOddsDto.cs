namespace HorseRacingPrediction.Contracts;

public sealed record RaceOddsDto(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<RaceWinOddsDto> WinOdds,
    IReadOnlyList<RacePlaceOddsDto> PlaceOdds);
