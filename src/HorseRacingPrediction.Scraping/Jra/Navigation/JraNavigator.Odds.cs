using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Navigation;

public sealed partial class JraNavigator
{
    public async Task<IJraPage> ToRaceOddsAsync(RaceId race, CancellationToken cancellationToken = default)
    {
        await ToRaceCardAsync(race, cancellationToken).ConfigureAwait(false);
        var links = await _browser.GetLinksAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var odds = links.FirstOrDefault(x => x.Title.Contains("オッズ", StringComparison.Ordinal));
        if (odds is null) throw new JraNavigationException("対象レースのオッズリンクが見つかりません。",
            JraNavigationFailureReason.NotYetPublished);
        await _browser.ClickLinkAsync(odds, cancellationToken).ConfigureAwait(false);
        return await _pageReader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }
}
