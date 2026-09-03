using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>
/// 現在ブラウザーに表示されているページを解析する唯一の入口。薄く保つ。
/// </summary>
public sealed class JraPageReader
{
    private readonly IWebBrowser _browser;
    private readonly IReadOnlyList<IJraPageParser> _parsers;
    private readonly ILogger<JraPageReader> _logger;

    public JraPageReader(
        IWebBrowser browser,
        IEnumerable<IJraPageParser> parsers,
        ILogger<JraPageReader>? logger = null)
    {
        _browser = browser;

        _parsers = parsers
            .OrderByDescending(x => x.Priority)
            .ToArray();

        _logger = logger ?? NullLogger<JraPageReader>.Instance;
    }

    public async Task<IJraPage> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot =
            await _browser.GetPageSnapshotAsync(
                cancellationToken: cancellationToken);

        foreach (var parser in _parsers)
        {
            if (parser.CanParse(snapshot))
            {
                var page = parser.Parse(snapshot);

                _logger.LogDebug(
                    "JRA page detected. Kind={Kind} Url={Url}",
                    page.Kind,
                    page.Url);

                return page;
            }
        }

        _logger.LogDebug(
            "JRA page unrecognized. Url={Url}",
            snapshot.Url);

        return new JraUnknownPage(
            snapshot.Url,
            snapshot.Title);
    }
}
