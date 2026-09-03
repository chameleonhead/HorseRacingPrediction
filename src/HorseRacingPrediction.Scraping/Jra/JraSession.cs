using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Jra;

/// <summary>
/// JRAサイト操作の入口となるファサード。
/// ページ遷移は <see cref="Navigate"/>、現在ページの取得は <see cref="Pages"/> を使う。
/// セッションが使う <see cref="IWebBrowser"/> の所有権はこのインスタンスが持ち、
/// <see cref="DisposeAsync"/> で破棄する。呼び出し側へBrowserを公開しない。
/// </summary>
public sealed class JraSession : IAsyncDisposable
{
    private readonly IWebBrowser _browser;

    public JraSession(
        IWebBrowser browser,
        IJraNavigator navigator,
        JraPageReader pageReader)
    {
        _browser = browser;
        Navigate = navigator;
        Pages = pageReader;
    }

    public IJraNavigator Navigate { get; }

    public JraPageReader Pages { get; }

    public ValueTask DisposeAsync()
        => _browser.DisposeAsync();
}
