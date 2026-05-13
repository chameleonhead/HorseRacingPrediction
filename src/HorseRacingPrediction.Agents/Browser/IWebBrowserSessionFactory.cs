namespace HorseRacingPrediction.Agents.Browser;

public interface IWebBrowserSessionFactory
{
    Task<IWebBrowser> CreateAsync(CancellationToken cancellationToken = default);
}