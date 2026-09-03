using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

[TestClass]
public sealed class JraSessionFactoryTests
{
    [TestMethod]
    public async Task CreateAsync_BrowserSessionFactoryを1回だけ呼び出す()
    {
        var browserFactory = new FakeWebBrowserSessionFactory();
        var factory = new JraSessionFactory(browserFactory, []);

        await using var session = await factory.CreateAsync();

        Assert.AreEqual(1, browserFactory.CreateCallCount);
    }

    [TestMethod]
    public async Task CreateAsync_JraSessionを生成する()
    {
        var browserFactory = new FakeWebBrowserSessionFactory();
        var factory = new JraSessionFactory(browserFactory, []);

        await using var session = await factory.CreateAsync();

        Assert.IsNotNull(session);
        Assert.IsNotNull(session.Navigate);
        Assert.IsNotNull(session.Pages);
    }

    [TestMethod]
    public async Task DisposeAsync_Browserがdisposeされる()
    {
        var browserFactory = new FakeWebBrowserSessionFactory();
        var factory = new JraSessionFactory(browserFactory, []);

        var session = await factory.CreateAsync();
        var browser = browserFactory.LastCreatedBrowser!;

        Assert.IsFalse(browser.IsDisposed);

        await session.DisposeAsync();

        Assert.IsTrue(browser.IsDisposed);
    }

    [TestMethod]
    public async Task CreateAsync_Session構築中に例外が起きた場合もBrowserがdisposeされる()
    {
        var browserFactory = new FakeWebBrowserSessionFactory();
        // JraPageReaderのコンストラクタにnullのパーサー列挙は許容されるため、
        // ここではJraPageReader/JraNavigator構築後段で必ず失敗するパーサーを渡し、
        // 構築処理そのものを模した例外経路をテストする代わりに、
        // Factory実装のcatch節を直接検証できるよう例外を投げるパーサー列挙を使う。
        var factory = new JraSessionFactory(browserFactory, new ThrowingParsers());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => factory.CreateAsync());

        var browser = browserFactory.LastCreatedBrowser!;
        Assert.IsTrue(browser.IsDisposed);
    }

    private sealed class ThrowingParsers : IEnumerable<IJraPageParser>
    {
        public IEnumerator<IJraPageParser> GetEnumerator()
            => throw new InvalidOperationException("parser enumeration failed");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
