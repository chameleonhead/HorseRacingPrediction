using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Scraping.Jra;

/// <summary>
/// <see cref="IWebBrowserSessionFactory"/> から生成したBrowserを使って
/// <see cref="JraSession"/> を組み立てる。Session構築の途中で例外が発生した場合は
/// 生成済みのBrowserを破棄してから例外を再送出するため、Browserのリークは起きない。
/// </summary>
public sealed class JraSessionFactory : IJraSessionFactory
{
    private readonly IWebBrowserSessionFactory _browserSessionFactory;
    private readonly IEnumerable<IJraPageParser> _pageParsers;
    private readonly ILoggerFactory _loggerFactory;

    public JraSessionFactory(
        IWebBrowserSessionFactory browserSessionFactory,
        IEnumerable<IJraPageParser> pageParsers,
        ILoggerFactory? loggerFactory = null)
    {
        _browserSessionFactory = browserSessionFactory;
        _pageParsers = pageParsers;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public async Task<JraSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        var browser = await _browserSessionFactory.CreateAsync(cancellationToken);

        try
        {
            // ここで列挙する（コンストラクタ時点では評価しない）ことで、パーサー列挙が
            // 失敗した場合もBrowserのdispose経路（catch節）を通す。
            var parsers = _pageParsers.ToArray();

            var pageReader = new JraPageReader(
                browser,
                parsers,
                _loggerFactory.CreateLogger<JraPageReader>());

            var navigator = new JraNavigator(
                browser,
                pageReader,
                _loggerFactory.CreateLogger<JraNavigator>());

            return new JraSession(browser, navigator, pageReader);
        }
        catch
        {
            await browser.DisposeAsync();
            throw;
        }
    }
}
