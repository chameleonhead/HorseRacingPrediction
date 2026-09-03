using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

/// <summary>
/// <see cref="IWebBrowserSessionFactory"/>のテスト用フェイク。呼び出しごとに新しい
/// <see cref="FakeWebBrowser"/>を生成し、最後に生成したインスタンスを検証用に公開する。
/// </summary>
internal sealed class FakeWebBrowserSessionFactory : IWebBrowserSessionFactory
{
    public int CreateCallCount { get; private set; }

    public FakeWebBrowser? LastCreatedBrowser { get; private set; }

    public Task<IWebBrowser> CreateAsync(CancellationToken cancellationToken = default)
    {
        CreateCallCount++;
        var browser = new FakeWebBrowser();
        LastCreatedBrowser = browser;
        return Task.FromResult<IWebBrowser>(browser);
    }
}
