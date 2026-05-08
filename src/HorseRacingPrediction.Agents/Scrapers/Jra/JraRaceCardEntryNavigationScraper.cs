using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 出馬表エントリーページ（keiba/ または thisweek/）から、
/// 実際の出馬表ページへ遷移するためのナビゲーションスクレイパー。
/// </summary>
public sealed class JraRaceCardEntryNavigationScraper : IScraper<JraRaceCardNavigationData>
{
    private readonly IWebBrowser _browser;
    private readonly JraRaceCardTopPageScraper _topPageScraper;
    private readonly JraRaceCardRaceListScraper _raceListScraper;

    public JraRaceCardEntryNavigationScraper(IWebBrowser browser)
    {
        _browser = browser;
        _topPageScraper = new JraRaceCardTopPageScraper(browser);
        _raceListScraper = new JraRaceCardRaceListScraper(browser);
    }

    public async Task<JraRaceCardNavigationData?> ScrapeAsync(
        string entryUrl,
        CancellationToken cancellationToken = default)
    {
        var holdingLabels = await _topPageScraper.ScrapeAsync(entryUrl, cancellationToken);
        if (holdingLabels is not null && holdingLabels.Count > 0)
        {
            var selectedHolding = holdingLabels[0];
            var raceNumbers = await _raceListScraper.ScrapeAsync(selectedHolding, cancellationToken);
            if (raceNumbers is null || raceNumbers.Count == 0)
            {
                return null;
            }

            var selectedRaceNumber = raceNumbers[0];
            await _browser.ClickAsync($"{selectedRaceNumber}レース", cancellationToken);

            var raceCardUrl = _browser.CurrentUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raceCardUrl))
            {
                return null;
            }

            return new JraRaceCardNavigationData(
                EntryUrl: entryUrl,
                RaceCardUrl: raceCardUrl,
                HoldingLabels: holdingLabels,
                SelectedHoldingLabel: selectedHolding,
                RaceNumbers: raceNumbers,
                SelectedRaceNumber: selectedRaceNumber,
                IsDirectFromThisWeek: false);
        }

        if (entryUrl.Contains("/keiba/thisweek", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _browser.ClickAsync("出馬表", cancellationToken);
            }
            catch
            {
                return null;
            }

            var raceCardUrl = _browser.CurrentUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raceCardUrl) || !IsLikelyRaceCardUrl(raceCardUrl))
            {
                return null;
            }

            return new JraRaceCardNavigationData(
                EntryUrl: entryUrl,
                RaceCardUrl: raceCardUrl,
                HoldingLabels: [],
                SelectedHoldingLabel: null,
                RaceNumbers: [],
                SelectedRaceNumber: null,
                IsDirectFromThisWeek: true);
        }

        return null;
    }

    private static bool IsLikelyRaceCardUrl(string url)
    {
        return url.Contains("/syutsuba", StringComparison.OrdinalIgnoreCase)
            || url.Contains("accessD.html", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record JraRaceCardNavigationData(
    string EntryUrl,
    string RaceCardUrl,
    IReadOnlyList<string> HoldingLabels,
    string? SelectedHoldingLabel,
    IReadOnlyList<int> RaceNumbers,
    int? SelectedRaceNumber,
    bool IsDirectFromThisWeek);