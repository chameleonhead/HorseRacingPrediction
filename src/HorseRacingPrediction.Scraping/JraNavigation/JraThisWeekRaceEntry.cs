namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraThisWeekRaceEntry(
    DateOnly? RaceDate,
    string RaceName,
    string? Grade,
    string? Racecourse,
    string? Distance,
    string? SpecialPageUrl,
    string? RaceCardUrl,
    string? HorseInfoUrl,
    string? DataUrl,
    string? RatingUrl,
    string? PlaybackUrl);