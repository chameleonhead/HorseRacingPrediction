using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Agents.Browser;

public sealed class PlaywrightWebBrowserSessionFactory : IWebBrowserSessionFactory
{
    private readonly ILogger<PlaywrightWebBrowser> _logger;

    public PlaywrightWebBrowserSessionFactory(ILogger<PlaywrightWebBrowser>? logger = null)
    {
        _logger = logger ?? NullLogger<PlaywrightWebBrowser>.Instance;
    }

    public Task<IWebBrowser> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CreateBrowserAsync();
    }

    private async Task<IWebBrowser> CreateBrowserAsync()
        => await PlaywrightWebBrowser.CreateAsync(logger: _logger).ConfigureAwait(false);
}