namespace HorseRacingPrediction.Scraping.Browser;

public interface IWebBrowserSessionFactory
{
    Task<IWebBrowser> CreateAsync(CancellationToken cancellationToken = default);
}