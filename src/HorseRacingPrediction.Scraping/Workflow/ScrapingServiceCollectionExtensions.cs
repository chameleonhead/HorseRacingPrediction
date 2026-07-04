using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Scrapers.Jra;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Scraping.Workflow;

/// <summary>
/// JRA 収集ワークフロー（機械的スクレイピング、LLM 不使用）を DI コンテナに登録する拡張メソッドを提供する。
/// </summary>
public static class ScrapingServiceCollectionExtensions
{
    /// <summary>
    /// <see cref="JraRaceResultCollectionWorkflow"/>、<see cref="JraNavigation.JraRaceResultUrlDiscoverer"/>、
    /// および <see cref="JraRaceResultScraper"/> を DI コンテナに登録する。
    /// <para>
    /// このワークフローは <see cref="JraNavigation.JraRaceResultUrlDiscoverer"/> がブラウザ操作で成績 URL を発見し、
    /// Playwright が各ページをスクレイプして DB へ保存するという、機械的なパイプラインである。
    /// </para>
    /// </summary>
    public static IServiceCollection AddJraRaceResultCollectionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<JraRaceResultScraper>(sp =>
        {
            var browser = sp.GetRequiredService<IWebBrowser>();
            return new JraRaceResultScraper(browser);
        });
        services.AddTransient<JraRaceResultCollectionWorkflow>(sp =>
            new JraRaceResultCollectionWorkflow(
                sp.GetRequiredService<IWebBrowser>(),
                sp.GetRequiredService<JraRaceResultScraper>(),
                sp.GetRequiredService<DataCollectionWriteTools>(),
                sp.GetRequiredService<IRaceQueryService>(),
                sp.GetService<ILogger<JraRaceResultCollectionWorkflow>>(),
                sp.GetService<ILoggerFactory>()));
        return services;
    }

    /// <summary>
    /// <see cref="JraRaceScheduleCollectionWorkflow"/> を DI コンテナに登録する。
    /// <para>
    /// JRA サイト構成に沿ったクリック遷移で、今後開催予定の開催日一覧を収集する。
    /// </para>
    /// </summary>
    public static IServiceCollection AddJraRaceScheduleCollectionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<JraRaceScheduleCollectionWorkflow>();
        return services;
    }

    /// <summary>
    /// <see cref="JraRaceCardCollectionWorkflow"/>、<see cref="JraNavigation.JraRaceCardUrlDiscoverer"/>、
    /// および <see cref="JraRaceCardScraper"/> を DI コンテナに登録する。
    /// <para>
    /// このワークフローは <see cref="JraNavigation.JraRaceCardUrlDiscoverer"/> がブラウザ操作で出馬表 URL を発見し、
    /// Playwright が各ページをスクレイプして DB へ保存するという、機械的なパイプラインである。
    /// </para>
    /// </summary>
    public static IServiceCollection AddJraRaceCardCollectionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<JraRaceCardScraper>(sp =>
        {
            var browser = sp.GetRequiredService<IWebBrowser>();
            return new JraRaceCardScraper(browser);
        });
        services.AddTransient<JraRaceCardCollectionWorkflow>(sp =>
        {
            var browser = sp.GetRequiredService<IWebBrowser>();

            return new JraRaceCardCollectionWorkflow(
                browser,
                sp.GetRequiredService<JraRaceCardScraper>(),
                sp.GetRequiredService<DataCollectionWriteTools>());
        });
        return services;
    }
}
