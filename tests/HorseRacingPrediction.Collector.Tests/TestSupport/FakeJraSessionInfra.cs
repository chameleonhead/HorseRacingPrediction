using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using SemanticPageSnapshot = HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot;

namespace HorseRacingPrediction.Collector.Tests.TestSupport;

/// <summary>
/// CollectionPlatform の収集ハンドラーが利用する
/// 統合テスト用。実際のPlaywright操作は一切行わず、<see cref="JraSession"/> を組み立てるためだけの
/// 最小限のダミー実装。Navigate/Pagesは、テストでWorkflowファクトリを差し替えて使わない限り
/// 呼ばれない想定であり、呼ばれた場合は明示的に失敗させる。
/// </summary>
internal class NoOpWebBrowser : IWebBrowser
{
    public bool IsDisposed { get; private set; }

    public string? CurrentUrl => null;

    public virtual ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> SelectOptionAsync(string fieldText, string optionText, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> ClickActionInSectionAsync(string sectionText, string actionText, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<SemanticPageSnapshot> GetPageSnapshotAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(int maxResults = 0, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// <see cref="IJraNavigator"/> のダミー実装。<see cref="RaceListResult"/> が設定されている場合のみ
/// <see cref="ToRaceListAsync"/> がそれを返す（CollectionPlatform の成績収集経路が
/// レース一覧ページを直接参照するため）。他のメソッドは呼ばれたら失敗する。
/// </summary>
internal sealed class FakeJraNavigator : IJraNavigator
{
    public Func<Uri, IJraPage>? DirectUrlFactory { get; set; }
    public List<Uri> DirectUrlRequests { get; } = [];
    public Task<IJraPage> ToUrlAsync(Uri url, CancellationToken cancellationToken = default)
    {
        DirectUrlRequests.Add(url);
        return Task.FromResult(DirectUrlFactory?.Invoke(url) ?? throw new NotSupportedException());
    }
    public Func<JraSubjectIdentity, JraSubjectPage>? SubjectFactory { get; set; }
    public Func<JraSubjectPage, JraSubjectPage?>? NextHistoryFactory { get; set; }
    public Func<JraSubjectIdentity, HorseHistoryRaceLink, JraRaceResultPage>? HistoryResultFactory { get; set; }
    public Task<JraSubjectPage> ToSubjectProfileAsync(JraSubjectIdentity subject, CancellationToken cancellationToken = default)
        => Task.FromResult(SubjectFactory?.Invoke(subject) ?? throw new NotSupportedException());
    public Task<JraSubjectPage?> NextHorseHistoryPageAsync(JraSubjectPage current, CancellationToken cancellationToken = default)
        => Task.FromResult(NextHistoryFactory?.Invoke(current));
    public Task<JraRaceResultPage> ToHorseHistoryResultAsync(JraSubjectIdentity subject, HorseHistoryRaceLink race, CancellationToken cancellationToken = default)
        => Task.FromResult(HistoryResultFactory?.Invoke(subject, race) ?? throw new NotSupportedException());
    public IJraPage? RaceListResult { get; set; }
    public Func<DateOnly, RaceCourse, IJraPage>? RaceCardListFactory { get; set; }
    public List<(DateOnly Date, RaceCourse Course)> RaceCardListRequests { get; } = [];
    public List<(DateOnly Date, RaceCourse Course)> RaceResultListRequests { get; } = [];

    /// <summary>
    /// <see cref="ToRaceResultListAsync"/> の戻り値/例外を(日付, 競馬場)ごとに差し替えたい
    /// テスト向けのフック。未設定の場合は <see cref="RaceListResult"/> にフォールバックする。
    /// </summary>
    public Func<DateOnly, RaceCourse, IJraPage>? RaceResultListFactory { get; set; }

    public Task<IJraPage> ToKeibaTopAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IJraPage> ToCalendarAsync(YearMonth month, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IJraPage> ToRaceListAsync(DateOnly date, RaceCourse course, CancellationToken cancellationToken = default)
    {
        RaceCardListRequests.Add((date, course));
        if (RaceCardListFactory is not null)
            return Task.FromResult(RaceCardListFactory(date, course));
        return RaceListResult is not null ? Task.FromResult(RaceListResult) : throw new NotSupportedException();
    }

    public Task<IJraPage> ToRaceResultListAsync(DateOnly date, RaceCourse course, CancellationToken cancellationToken = default)
    {
        RaceResultListRequests.Add((date, course));
        if (RaceResultListFactory is not null)
        {
            return Task.FromResult(RaceResultListFactory(date, course));
        }

        return RaceListResult is not null
            ? Task.FromResult(RaceListResult)
            : throw new NotSupportedException();
    }

    public Task<IJraPage> ToRaceCardAsync(RaceId race, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IJraPage> ToRaceResultAsync(RaceId race, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Func<RaceId, IJraPage>? RaceOddsFactory { get; set; }
    public Task<IJraPage> ToRaceOddsAsync(RaceId race, CancellationToken cancellationToken = default)
        => Task.FromResult(RaceOddsFactory?.Invoke(race) ?? throw new NotSupportedException());

    public Task<IJraPage> ToHistoricalRaceSearchAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public bool RaceCardLookupPeriodResult { get; set; } = true;

    public bool IsWithinRaceCardLookupPeriod(DateOnly date)
        => RaceCardLookupPeriodResult;
}

/// <summary>
/// <see cref="IJraSessionFactory"/> のダミー実装。呼び出しごとに新しい <see cref="JraSession"/> を
/// (使い捨ての <see cref="NoOpWebBrowser"/> を内包させて) 生成する。
/// </summary>
internal sealed class FakeJraSessionFactory : IJraSessionFactory
{
    public int CreateCallCount { get; private set; }

    public FakeJraNavigator? LastNavigator { get; private set; }

    public Func<FakeJraNavigator>? ConfigureNavigator { get; set; }

    public Task<JraSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        CreateCallCount++;
        var browser = new NoOpWebBrowser();
        var navigator = ConfigureNavigator?.Invoke() ?? new FakeJraNavigator();
        LastNavigator = navigator;
        var pageReader = new JraPageReader(browser, Array.Empty<IJraPageParser>());
        return Task.FromResult(new JraSession(browser, navigator, pageReader));
    }
}
