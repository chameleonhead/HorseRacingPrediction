using System.Text.RegularExpressions;
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

        // Task16実サイト確認で判明: 競馬メニュー配下の主要項目（出馬表・レース結果等）は
        // href を持つ通常のリンクではなくクリックで遷移するJS要素であるため、
        // GetLinksAsync によるURL探索ではなく ClickAsync を使う必要がある。
        // 「過去レース結果検索」も同じメニュー階層に表示されるボタンであり、同様の
        // 前提で ClickAsync に切り替えた（このボタン自体からの遷移先ページ構造・
        // フォーム項目名は本タスクでは実サイトに対して未検証）。
        await NavigateToRaceResultTopAsync(cancellationToken);
        await _browser.ClickAsync(
            JraNavigationLinks.HistoricalRaceSearch[0],
            cancellationToken);

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
    /// 「現在開催週」とみなす期間。レース日が今日から前後3日以内であれば
    /// 現在開催中とみなす（暫定値）。
    ///
    /// Task 16（実サイトE2Eテスト、2026-09-04実施）で判明した事項:
    /// 実サイトでは「現在開催」と「直近の過去開催」は同一の「レース結果 開催選択」
    /// ページ（/JRADB/accessS.html）内に、開催回・競馬場・開催日番号のボタン
    /// （例：「4回中山1日」）として一緒に列挙されている。ページ内に別タブ・別リンクは
    /// 存在しないため、Current/Recentのどちらであっても遷移方法は同一であり、
    /// このしきい値は「同じページに載っている範囲かどうか」の目安に過ぎない
    /// （ページに載っていない古い開催は <see cref="ToHistoricalRaceResultAsync"/> の
    /// 「過去レース結果検索」フォームへフォールバックする）。
    /// </summary>
    private bool IsCurrentRacePeriod(DateOnly raceDate)
        => Math.Abs(raceDate.DayNumber - _today().DayNumber) <= 3;

    /// <summary>
    /// 「最近の過去開催」とみなす期間。今日からおよそ3ヶ月（92日）以内であれば
    /// 「最近」とみなす（暫定値）。
    ///
    /// Task 16（実サイトE2Eテスト、2026-09-04実施）で判明した事項:
    /// 「過去のレース結果」は実サイト上ではクリック可能なリンク/ボタンではなく、
    /// 「レース結果 開催選択」ページ内の一区画を示す見出しに過ぎない。同じページ内に
    /// 直近8週間以上の開催日ボタンがそのまま列挙されており、追加のクリック操作なしで
    /// 対象日・競馬場のボタンへ直接遷移できることを実サイトで確認した。
    /// そのため <see cref="ToCurrentRaceResultAsync"/> と <see cref="ToRecentRaceResultAsync"/>
    /// は同一の <see cref="ClickMeetingButtonAsync"/> 呼び出しに帰着する。
    /// 92日というしきい値そのものが実際のページ掲載期間と一致しているかは未検証であり、
    /// 掲載範囲を超える古い開催日は <see cref="ToHistoricalRaceResultAsync"/>
    /// （過去レース結果検索フォーム）へのフォールバックが必要になるが、このフォーム経路は
    /// 本タスクでは実サイトに対して検証できていない（要フォローアップ）。
    /// </summary>
    private bool IsRecentRacePeriod(DateOnly raceDate)
        => Math.Abs(raceDate.DayNumber - _today().DayNumber) <= 92;

    private async Task<IJraPage> ToCurrentRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken)
    {
        // 競馬トップ → レース結果 → 対象日・競馬場（開催選択ボタン） → 対象R
        await NavigateToRaceResultTopAsync(cancellationToken);

        await ClickMeetingButtonAsync(
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
        // 実サイト確認の結果、「過去のレース結果」はページ内の見出しであり
        // クリック可能なリンクではない。現在開催・直近開催とも同一の
        // 「レース結果 開催選択」ページに開催ボタンが並ぶため、Currentと全く同じ
        // 遷移で到達できる（Task16実サイト確認で判明）。
        await NavigateToRaceResultTopAsync(cancellationToken);

        await ClickMeetingButtonAsync(
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
        // Task16実サイト確認で判明: 競馬メニューの「レース結果」はhrefを持つ通常の
        // リンクではなく、クリックで遷移するJS要素（同一URL上でPOSTしたかのように
        // 内容が切り替わる）。GetLinksAsync では検出できないため ClickAsync を使う。
        await _browser.NavigateAsync(
            JraUrls.KeibaTop,
            cancellationToken);

        await _browser.ClickAsync(
            JraNavigationLinks.RaceResult[0],
            cancellationToken);
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

    /// <summary>
    /// 「出馬表」メニューから開催選択ページを経由して、対象日・競馬場のレース一覧
    /// （出馬表 レース選択ページ）へ遷移する。
    ///
    /// Task16実サイト確認で判明: /keiba/calendar/ の開催日程ページはテーブルセルが
    /// プレーンテキストのみで、日付・競馬場からリンクを辿ることは一切できない
    /// （カレンダーは「その日・競馬場が開催されているか」を確認する用途のみに使う）。
    /// 実際にレース一覧へ辿り着く導線は、競馬メニューの「出馬表」からクリック遷移する
    /// 「出馬表 開催選択」ページ（/JRADB/accessD.html）上の、開催回・競馬場・開催日番号を
    /// 表すボタン（例：「4回中山1日」）であり、これらもhrefを持たないJS要素である。
    /// </summary>
    private async Task NavigateToRaceDateCourseAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken)
    {
        await _browser.NavigateAsync(
            JraUrls.KeibaTop,
            cancellationToken);

        await _browser.ClickAsync(
            JraNavigationLinks.RaceCard[0],
            cancellationToken);

        await ClickMeetingButtonAsync(
            date,
            course,
            cancellationToken);
    }

    /// <summary>
    /// 「出馬表 開催選択」または「レース結果 開催選択」ページ上で、対象日・競馬場に
    /// 対応する開催ボタン（例：「4回中山1日」）を探してクリックする。
    /// ボタンはhrefを持たず、日付見出し（例：「9月5日（土曜）」）の直後に開催回・
    /// 競馬場・開催日番号のテキストが並ぶ形式のため、ページ本文のテキストから
    /// 該当ボタンの文字列そのものを特定し、その文字列で ClickAsync する
    /// （Task16実サイト確認で判明）。
    /// </summary>
    private async Task ClickMeetingButtonAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken)
    {
        var courseName =
            RaceCourseName(course);

        var snapshot =
            await _browser.GetPageSnapshotAsync(
                cancellationToken: cancellationToken);

        var buttonText =
            FindMeetingButtonText(snapshot.MainText, date, courseName);

        if (buttonText is null)
        {
            throw new JraNavigationException(
                $"{date:yyyy-MM-dd} {course} の開催選択ボタンが見つかりませんでした。");
        }

        await _browser.ClickAsync(
            buttonText,
            cancellationToken);
    }

    private static readonly Regex DateHeaderRegex =
        new(@"\d{1,2}月\d{1,2}日", RegexOptions.Compiled);

    /// <summary>
    /// 「開催選択」ページ本文テキストから、対象日の見出し直後～次の日付見出しまでの
    /// 範囲を切り出し、その中から「n回{競馬場名}m日」形式の開催ボタン文字列を探す。
    /// </summary>
    internal static string? FindMeetingButtonText(
        string mainText,
        DateOnly date,
        string courseName)
    {
        var dateMarker =
            $"{date.Month}月{date.Day}日";

        var startIndex =
            mainText.IndexOf(dateMarker, StringComparison.Ordinal);

        if (startIndex < 0)
        {
            return null;
        }

        var searchStart =
            startIndex + dateMarker.Length;

        var nextDateMatch =
            DateHeaderRegex.Match(mainText, searchStart);

        var endIndex =
            nextDateMatch.Success ? nextDateMatch.Index : mainText.Length;

        var segment =
            mainText[searchStart..endIndex];

        var buttonRegex =
            new Regex($@"\d+回{Regex.Escape(courseName)}\d+日");

        var match =
            buttonRegex.Match(segment);

        return match.Success ? match.Value : null;
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
