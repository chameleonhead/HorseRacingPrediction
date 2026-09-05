using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

public sealed record JraRaceResultPage(
    string Url,
    RaceId RaceId,
    string? RaceName,
    IReadOnlyList<RaceResultEntry> Results,
    string? WeatherText = null,
    string? TrackConditionText = null,
    RacePayouts? Payouts = null)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.RaceResult;
}

public sealed record PayoutLine(string Combination, decimal Amount);

public sealed record RacePayouts(
    IReadOnlyList<PayoutLine> WinPayouts,
    IReadOnlyList<PayoutLine> PlacePayouts,
    IReadOnlyList<PayoutLine> QuinellaPayouts,
    IReadOnlyList<PayoutLine> ExactaPayouts,
    IReadOnlyList<PayoutLine> TrifectaPayouts)
{
    public bool IsEmpty =>
        WinPayouts.Count == 0 && PlacePayouts.Count == 0 && QuinellaPayouts.Count == 0 &&
        ExactaPayouts.Count == 0 && TrifectaPayouts.Count == 0;
}
