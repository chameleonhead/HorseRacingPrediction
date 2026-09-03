using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Jra;

/// <summary>
/// JRAサイト操作の入口となるファサード。
/// ページ遷移は <see cref="Navigate"/>、現在ページの取得は <see cref="Pages"/> を使う。
/// </summary>
public sealed class JraSession
{
    public JraSession(
        IJraNavigator navigator,
        JraPageReader pageReader)
    {
        Navigate = navigator;
        Pages = pageReader;
    }

    public IJraNavigator Navigate { get; }

    public JraPageReader Pages { get; }
}