using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Scraping.Jra;

/// <summary>
/// 指定日・開催競馬場の出馬表を収集する <see cref="IJraRaceCardCollectionWorkflow"/> を
/// <see cref="JraSession"/> から組み立てるファクトリデリゲート。
/// </summary>
public delegate IJraRaceCardCollectionWorkflow JraRaceCardCollectionWorkflowFactory(JraSession session);

/// <summary>
/// 指定レースの確定成績を収集する <see cref="IJraRaceResultCollectionWorkflow"/> を
/// <see cref="JraSession"/> から組み立てるファクトリデリゲート。
/// </summary>
public delegate IJraRaceResultCollectionWorkflow JraRaceResultCollectionWorkflowFactory(JraSession session);

/// <summary>
/// 指定日の開催競馬場を特定する <see cref="IJraScheduleCollectionWorkflow"/> を
/// <see cref="JraSession"/> から組み立てるファクトリデリゲート。
/// </summary>
public delegate IJraScheduleCollectionWorkflow JraScheduleCollectionWorkflowFactory(JraSession session);

/// <summary>
/// JRAサイトスクレイピング層（Browser/Navigator/Parser/Session/Workflow）を DI コンテナに登録する。
/// </summary>
/// <remarks>
/// <see cref="JraScheduleCollectionWorkflow"/>/<see cref="JraRaceCardCollectionWorkflow"/>/
/// <see cref="JraRaceResultCollectionWorkflow"/> は、収集1回ごとに使い捨てる
/// <see cref="JraSession"/>（内部に <see cref="IWebBrowser"/> を所有し、収集完了後
/// <c>await using</c> で破棄される）をコンストラクタ引数に取る。そのため各Workflowの
/// インスタンス自体はコンテナに直接登録できず、<see cref="JraSession"/> を受け取って
/// Workflowを組み立てるファクトリデリゲートとして登録する。ファクトリ自体は状態を持たないため
/// Singletonで問題ないが、都度取得する依存（<see cref="IDataCollectionWriteService"/>）は
/// ファクトリ呼び出し時に解決することでキャプティブ依存を避けている。
/// </remarks>
public static class JraScrapingServiceCollectionExtensions
{
    public static IServiceCollection AddJraScraping(this IServiceCollection services)
    {
        services.AddSingleton<IWebBrowserSessionFactory, PlaywrightWebBrowserSessionFactory>();

        services.AddSingleton<IJraPageParser, CalendarPageParser>();
        services.AddSingleton<IJraPageParser, RaceListPageParser>();
        services.AddSingleton<IJraPageParser, RaceCardPageParser>();
        services.AddSingleton<IJraPageParser, RaceResultPageParser>();

        // JraSessionFactory の依存(IWebBrowserSessionFactory/IJraPageParser/ILoggerFactory)は
        // いずれもSingletonであり、JraSessionFactory自身も呼び出しごとの状態を持たないためSingletonとする。
        // 生成される JraSession 自体はSingletonではなく、CreateAsync の呼び出しごとに使い捨てで生成される。
        services.AddSingleton<IJraSessionFactory, JraSessionFactory>();

        // Workflowはリクエストごとに生成される JraSession を受け取るため、コンテナには
        // 「Sessionを受け取ってWorkflowを組み立てる」ファクトリデリゲートとして登録する。
        services.AddSingleton<JraScheduleCollectionWorkflowFactory>(
            _ => session => new JraScheduleCollectionWorkflow(session));

        services.AddSingleton<JraRaceCardCollectionWorkflowFactory>(sp =>
            session => new JraRaceCardCollectionWorkflow(
                session,
                sp.GetRequiredService<IDataCollectionWriteService>()));

        services.AddSingleton<JraRaceResultCollectionWorkflowFactory>(sp =>
            session => new JraRaceResultCollectionWorkflow(
                session,
                sp.GetRequiredService<IDataCollectionWriteService>()));

        return services;
    }
}
