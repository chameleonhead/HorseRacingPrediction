using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Scraping.Jra.Navigation;

/// <summary>
/// JRAサイト内のページ遷移を担う。どのページからでもリンク経由で目的ページへ
/// 遷移できるようにし、見つからなければ競馬トップへフォールバックする。
/// </summary>
public sealed class JraNavigator
    : IJraNavigator
{
    private readonly IWebBrowser _browser;
    private readonly JraPageReader _pageReader;
    private readonly ILogger<JraNavigator> _logger;
    private readonly Func<DateOnly> _today;

    public JraNavigator(
        IWebBrowser browser,
        JraPageReader pageReader,
        ILogger<JraNavigator>? logger = null)
        : this(browser, pageReader, logger, today: null)
    {
    }

    /// <summary>
    /// テスト用に「今日の日付」を差し替え可能にするコンストラクタ。
    /// </summary>
    internal JraNavigator(
        IWebBrowser browser,
        JraPageReader pageReader,
        ILogger<JraNavigator>? logger,
        Func<DateOnly>? today)
    {
        _browser = browser;
        _pageReader = pageReader;
        _logger = logger ?? NullLogger<JraNavigator>.Instance;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
    }

    public async Task<IJraPage> ToKeibaTopAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "JRA navigation start. Destination=KeibaTop CurrentUrl={CurrentUrl}",
            _browser.CurrentUrl);

        await _browser.NavigateAsync(
            JraUrls.KeibaTop,
            cancellationToken);

        return await _pageReader.ReadAsync(
            cancellationToken);
    }

    public async Task<IJraPage> ToCalendarAsync(
        YearMonth month,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "JRA navigation start. Destination=Calendar Month={Month} CurrentUrl={CurrentUrl}",
            month,
            _browser.CurrentUrl);

        if (!await TryNavigateByLinkAsync(
                JraNavigationLinks.Calendar,
                cancellationToken))
        {
            await _browser.NavigateAsync(
                JraUrls.Calendar,
                cancellationToken);
        }

        await SelectCalendarMonthAsync(
            month,
            cancellationToken);

        var page =
            await _pageReader.ReadAsync(
                cancellationToken);

        _logger.LogInformation(
            "JRA navigation done. Destination=Calendar ResolvedKind={Kind} Url={Url}",
            page.Kind,
            page.Url);

        return page;
    }

    public async Task<IJraPage> ToRaceListAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "JRA navigation start. Destination=RaceList Date={Date} Course={Course} CurrentUrl={CurrentUrl}",
            date,
            course,
            _browser.CurrentUrl);

        var calendarPage =
            await ToCalendarAsync(
                new YearMonth(date.Year, date.Month),
                cancellationToken);

        if (calendarPage is not JraCalendarPage calendar)
        {
            throw new JraNavigationException(
                $"カレンダーページを取得できませんでした。 Kind={calendarPage.Kind}");
        }

        JraRaceDate? raceDate =
            calendar.RaceDates
                .FirstOrDefault(x => x.Date == date);

        if (raceDate is null)
        {
            throw new JraNavigationException(
                $"{date:yyyy-MM-dd} に開催情報がありません。");
        }

        if (!raceDate.Courses.Contains(course))
        {
            throw new JraNavigationException(
                $"{date:yyyy-MM-dd} に {course} の開催情報がありません。");
        }

        await NavigateToRaceDateCourseAsync(
            date,
            course,
            cancellationToken);

        var page =
            await _pageReader.ReadAsync(
                cancellationToken);

        _logger.LogInformation(
            "JRA navigation done. Destination=RaceList ResolvedKind={Kind} Url={Url}",
            page.Kind,
            page.Url);

        return page;
    }

    public async Task<IJraPage> ToRaceCardAsync(
        RaceId race,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "JRA navigation start. Destination=RaceCard Race={Race} CurrentUrl={CurrentUrl}",
            race,
            _browser.CurrentUrl);

        var listPage =
            await ToRaceListAsync(
                race.Date,
                race.Course,
                cancellationToken);

        if (listPage is not JraRaceListPage raceList)
        {
            throw new JraNavigationException(
                $"レース一覧を取得できませんでした。 Kind={listPage.Kind}");
        }

        var summary =
            raceList.Races
                .SingleOrDefault(x => x.Id == race);

        if (summary is null)
        {
            throw new JraNavigationException(
                $"{race} がレース一覧に存在しません。");
        }

        if (!string.IsNullOrWhiteSpace(summary.RaceCardUrl))
        {
            var resolvedUrl =
                ResolveUrl(_browser.CurrentUrl, summary.RaceCardUrl)
                ?? throw new JraNavigationException(
                    $"URLを解決できません: {summary.RaceCardUrl}");

            await _browser.NavigateAsync(
                resolvedUrl,
                cancellationToken);
        }
        else
        {
            await NavigateRaceNumberLinkAsync(
                race.Number,
                JraNavigationLinks.RaceCard,
                cancellationToken);
        }

        var page =
            await _pageReader.ReadAsync(
                cancellationToken);

        _logger.LogInformation(
            "JRA navigation done. Destination=RaceCard ResolvedKind={Kind} Url={Url}",
            page.Kind,
            page.Url);

        return page;
    }

    public async Task<IJraPage> ToRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "JRA navigation start. Destination=RaceResult Race={Race} CurrentUrl={CurrentUrl}",
            race,
            _browser.CurrentUrl);

        IJraPage page;
        string route;

        if (IsCurrentRacePeriod(race.Date))
        {
            route = "Current";
            page = await ToCurrentRaceResultAsync(race, cancellationToken);
        }
        else if (IsRecentRacePeriod(race.Date))
        {
            route = "Recent";
            page = await ToRecentRaceResultAsync(race, cancellationToken);
        }
        else
        {
            route = "Historical";
            page = await ToHistoricalRaceResultAsync(race, cancellationToken);
        }

        _logger.LogInformation(
            "JRA navigation done. Destination=RaceResult Route={Route} ResolvedKind={Kind} Url={Url}",
            route,
            page.Kind,
            page.Url);

        return page;
    }

    public async Task<IJraPage> ToHistoricalRaceSearchAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "JRA navigation start. Destination=HistoricalRaceSearch CurrentUrl={CurrentUrl}",
            _browser.CurrentUrl);

        if (!await TryNavigateByLinkAsync(
                JraNavigationLinks.HistoricalRaceSearch,
                cancellationToken))
        {
            await _browser.NavigateAsync(
                JraUrls.KeibaTop,
                cancellationToken);

            if (!await TryNavigateByLinkAsync(
                    JraNavigationLinks.HistoricalRaceSearch,
                    cancellationToken))
            {
                throw new JraNavigationException(
                    "過去レース結果検索ページへ遷移できませんでした。");
            }
        }

        var page =
            await _pageReader.ReadAsync(
                cancellationToken);

        _logger.LogInformation(
            "JRA navigation done. Destination=HistoricalRaceSearch ResolvedKind={Kind} Url={Url}",
            page.Kind,
            page.Url);

        return page;
    }

    /// <summary>
    /// 「現在開催週」とみなす期間。実サイト確認前の暫定値であり、レース日が
    /// 今日から前後3日以内であれば現在開催中とみなす。実ページ確認後に固定する。
    ///
    /// Task 16（実サイトE2Eテスト、2026-09-04実施）で判明した事項:
    /// このしきい値そのものより手前の <see cref="NavigateToRaceDateCourseAsync"/> の
    /// 前提が実サイトと合っていない。/keiba/calendar/ の開催日程ページは各週・各競馬場への
    /// クリック可能なリンクを一切含まない（開催日と競馬場名が並ぶ静的なテキスト/表のみ）。
    /// 実際に日付・競馬場を選んでレース一覧へ辿り着く導線は、競馬メニューの「出馬表」
    /// 「レース結果」からJS遷移する /JRADB/accessD.html・accessS.html の「開催選択」
    /// ページ側にあり、そこでは当該週の重賞・特別レースのみが個別レースへの直接リンクとして
    /// 列挙され、開催日は見出し（タブ/アコーディオンの可能性）として表示されるのみで、
    /// 「日付+競馬場」というテキストの組み合わせでリンクを検索する現行実装の前提
    /// （<see cref="NavigateToRaceDateCourseAsync"/>）とは根本的に構造が異なる。
    /// 正しい導線を確立するには開催選択ページのタブ/アコーディオン操作を別途調査する必要が
    /// あり、本タスクの範囲では確定できなかったため、挙動を推測で書き換えることはせず、
    /// 判明した事実のみをここに記録する（現在週RaceList/RaceCard取得のE2Eテストは
    /// この導線不一致により実サイトに対して失敗する）。
    /// </summary>
    private bool IsCurrentRacePeriod(DateOnly raceDate)
        => Math.Abs(raceDate.DayNumber - _today().DayNumber) <= 3;

    /// <summary>
    /// 「最近の過去開催」とみなす期間。実サイト確認前の暫定値であり、
    /// 今日からおよそ3ヶ月（92日）以内であれば「最近」とみなす。実ページ確認後に固定する。
    ///
    /// Task 16（実サイトE2Eテスト、2026-09-04実施）で判明した事項:
    /// <see cref="JraNavigationLinks.RecentRaceResults"/>（"過去のレース結果"）のリンクは
    /// 実際の /JRADB/accessS.html（レース結果 開催選択）ページ上にクリック可能な要素として
    /// 存在しないことを確認した（IWebBrowser.ClickAsync が要素未検出の例外を送出）。
    /// 同ページは直近の重賞・特別レースへの直接リンクと、"8月23日 （日曜）" のような
    /// 過去開催日の見出しを列挙する構造であり、正しい「最近の過去開催」への導線は
    /// この見出し（タブ等）経由である可能性が高いが、本タスクの範囲では確定できなかった。
    /// このため <see cref="ToRecentRaceResultAsync"/> は実サイトに対して確実に失敗する
    /// （<see cref="JraNavigationException"/>）。しきい値の日数自体が正しいかも未検証であり、
    /// 併せて要フォローアップ。
    /// </summary>
    private bool IsRecentRacePeriod(DateOnly raceDate)
        => Math.Abs(raceDate.DayNumber - _today().DayNumber) <= 92;

    private async Task<IJraPage> ToCurrentRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken)
    {
        // 競馬トップ → レース結果 → 対象日・競馬場 → 対象R
        await NavigateToRaceResultTopAsync(cancellationToken);

        await NavigateToRaceDateCourseAsync(
            race.Date,
            race.Course,
            cancellationToken);

        await NavigateRaceNumberLinkAsync(
            race.Number,
            JraNavigationLinks.RaceResult,
            cancellationToken);

        return await _pageReader.ReadAsync(cancellationToken);
    }

    private async Task<IJraPage> ToRecentRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken)
    {
        // 競馬トップ → レース結果 → 過去のレース結果 → 対象日・競馬場 → 対象R
        await NavigateToRaceResultTopAsync(cancellationToken);

        if (!await TryNavigateByLinkAsync(
                JraNavigationLinks.RecentRaceResults,
                cancellationToken))
        {
            throw new JraNavigationException(
                "過去のレース結果ページへ遷移できませんでした。");
        }

        await NavigateToRaceDateCourseAsync(
            race.Date,
            race.Course,
            cancellationToken);

        await NavigateRaceNumberLinkAsync(
            race.Number,
            JraNavigationLinks.RaceResult,
            cancellationToken);

        return await _pageReader.ReadAsync(cancellationToken);
    }

    private async Task<IJraPage> ToHistoricalRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken)
    {
        // 競馬トップ → レース結果 → 過去レース結果検索 → 検索フォーム → 検索結果 → 対象レース
        await ToHistoricalRaceSearchAsync(cancellationToken);

        await _browser.SetFieldValueAsync(
            "開催年",
            race.Date.Year.ToString(),
            cancellationToken);

        await _browser.SetFieldValueAsync(
            "開催月",
            race.Date.Month.ToString(),
            cancellationToken);

        await _browser.SetFieldValueAsync(
            "開催日",
            race.Date.Day.ToString(),
            cancellationToken);

        await _browser.SelectOptionAsync(
            "競馬場",
            RaceCourseName(race.Course),
            cancellationToken);

        await _browser.SubmitFormAsync(
            cancellationToken: cancellationToken);

        await NavigateRaceNumberLinkAsync(
            race.Number,
            JraNavigationLinks.RaceResult,
            cancellationToken);

        return await _pageReader.ReadAsync(cancellationToken);
    }

    private async Task NavigateToRaceResultTopAsync(
        CancellationToken cancellationToken)
    {
        if (!await TryNavigateByLinkAsync(
                JraNavigationLinks.RaceResult,
                cancellationToken))
        {
            await _browser.NavigateAsync(
                JraUrls.KeibaTop,
                cancellationToken);

            if (!await TryNavigateByLinkAsync(
                    JraNavigationLinks.RaceResult,
                    cancellationToken))
            {
                throw new JraNavigationException(
                    "レース結果ページへ遷移できませんでした。");
            }
        }
    }

    private async Task<bool> TryNavigateByLinkAsync(
        IReadOnlyList<string> candidateTexts,
        CancellationToken cancellationToken)
    {
        var links =
            await _browser.GetLinksAsync(
                cancellationToken: cancellationToken);

        foreach (var candidate in candidateTexts)
        {
            var link =
                links.FirstOrDefault(x =>
                    x.Title.Contains(
                        candidate,
                        StringComparison.Ordinal));

            if (link is null)
            {
                continue;
            }

            var url =
                ResolveUrl(_browser.CurrentUrl, link.Url);

            if (url is null)
            {
                continue;
            }

            await _browser.NavigateAsync(
                url,
                cancellationToken);

            return true;
        }

        return false;
    }

    private async Task SelectCalendarMonthAsync(
        YearMonth month,
        CancellationToken cancellationToken)
    {
        var page =
            await _pageReader.ReadAsync(
                cancellationToken);

        if (page is JraCalendarPage calendar &&
            calendar.Month == month)
        {
            return;
        }

        var links =
            await _browser.GetLinksAsync(
                cancellationToken: cancellationToken);

        var monthText =
            $"{month.Month}月";

        var target =
            links.FirstOrDefault(x =>
                x.Title.Contains(
                    monthText,
                    StringComparison.Ordinal));

        if (target is null)
        {
            throw new JraNavigationException(
                $"カレンダーの {month} へ遷移できませんでした。");
        }

        var url =
            ResolveUrl(_browser.CurrentUrl, target.Url)
            ?? throw new JraNavigationException(
                $"URLを解決できません: {target.Url}");

        await _browser.NavigateAsync(
            url,
            cancellationToken);
    }

    private async Task NavigateToRaceDateCourseAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken)
    {
        var courseName =
            RaceCourseName(course);

        var links =
            await _browser.GetLinksAsync(
                cancellationToken: cancellationToken);

        var dayMarkers = new[]
        {
            $"{date.Day}日",
            $"{date.Month}/{date.Day}",
            date.ToString("yyyy-MM-dd"),
        };

        var target =
            links.FirstOrDefault(x =>
                x.Title.Contains(courseName, StringComparison.Ordinal) &&
                dayMarkers.Any(marker =>
                    x.Title.Contains(marker, StringComparison.Ordinal)));

        if (target is null)
        {
            throw new JraNavigationException(
                $"{date:yyyy-MM-dd} {course} のレース一覧リンクが見つかりませんでした。");
        }

        var url =
            ResolveUrl(_browser.CurrentUrl, target.Url)
            ?? throw new JraNavigationException(
                $"URLを解決できません: {target.Url}");

        await _browser.NavigateAsync(
            url,
            cancellationToken);
    }

    private async Task NavigateRaceNumberLinkAsync(
        int raceNumber,
        IReadOnlyList<string> linkTextCandidates,
        CancellationToken cancellationToken)
    {
        var links =
            await _browser.GetLinksAsync(
                cancellationToken: cancellationToken);

        var numberMarkers = new[]
        {
            $"{raceNumber}R",
            $"{raceNumber}レース",
        };

        var target =
            links.FirstOrDefault(x =>
                numberMarkers.Any(marker =>
                    x.Title.Contains(marker, StringComparison.Ordinal)) &&
                linkTextCandidates.Any(candidate =>
                    x.Title.Contains(candidate, StringComparison.Ordinal)));

        target ??=
            links.FirstOrDefault(x =>
                numberMarkers.Any(marker =>
                    x.Title.Contains(marker, StringComparison.Ordinal)));

        if (target is null)
        {
            throw new JraNavigationException(
                $"{raceNumber}R のリンクが見つかりませんでした。");
        }

        var url =
            ResolveUrl(_browser.CurrentUrl, target.Url)
            ?? throw new JraNavigationException(
                $"URLを解決できません: {target.Url}");

        await _browser.NavigateAsync(
            url,
            cancellationToken);
    }

    private static string RaceCourseName(RaceCourse course)
        => course switch
        {
            RaceCourse.Sapporo => "札幌",
            RaceCourse.Hakodate => "函館",
            RaceCourse.Fukushima => "福島",
            RaceCourse.Niigata => "新潟",
            RaceCourse.Tokyo => "東京",
            RaceCourse.Nakayama => "中山",
            RaceCourse.Chukyo => "中京",
            RaceCourse.Kyoto => "京都",
            RaceCourse.Hanshin => "阪神",
            RaceCourse.Kokura => "小倉",
            _ => throw new ArgumentOutOfRangeException(nameof(course)),
        };

    internal static string? ResolveUrl(
        string? currentUrl,
        string href)
    {
        if (Uri.TryCreate(
                href,
                UriKind.Absolute,
                out var absolute))
        {
            return absolute.ToString();
        }

        if (currentUrl is not null &&
            Uri.TryCreate(currentUrl, UriKind.Absolute, out var current) &&
            Uri.TryCreate(current, href, out var resolved))
        {
            return resolved.ToString();
        }

        return null;
    }
}
