using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HorseRacingPrediction.Scraping.Browser.Snapshots;

namespace HorseRacingPrediction.Scraping.Browser;

public sealed class PlaywrightWebBrowserSessionFactory : IWebBrowserSessionFactory
{
    private readonly ILogger<PlaywrightWebBrowser> _logger;
    private readonly IPageSnapshotter _pageSnapshotter;

    public PlaywrightWebBrowserSessionFactory(
        IPageSnapshotter pageSnapshotter,
        ILogger<PlaywrightWebBrowser>? logger = null)
    {
        _pageSnapshotter = pageSnapshotter;
        _logger = logger ?? NullLogger<PlaywrightWebBrowser>.Instance;
    }

    public Task<IWebBrowser> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CreateBrowserAsync();
    }

    private async Task<IWebBrowser> CreateBrowserAsync()
        => await PlaywrightWebBrowser.CreateAsync(logger: _logger, pageSnapshotter: _pageSnapshotter).ConfigureAwait(false);
}
