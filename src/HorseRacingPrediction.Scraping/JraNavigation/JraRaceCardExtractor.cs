using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Scraping.JraNavigation;

/// <summary>
/// 出馬表ページから <see cref="JraRaceCardData"/> を抽出する。
/// 既存の <see cref="JraRaceCardScraper.ScrapeCurrentPageAsync"/> に委譲する。
/// </summary>
public sealed class JraRaceCardExtractor : IPageExtractor
{
    public JraPageKind[] SupportedPageKinds => [JraPageKind.RaceCard];

    public async Task<object?> ExtractAsync(IWebBrowser browser, CancellationToken cancellationToken = default)
    {
        var scraper = new JraRaceCardScraper(browser);
        return await scraper.ScrapeCurrentPageAsync(cancellationToken);
    }
}
