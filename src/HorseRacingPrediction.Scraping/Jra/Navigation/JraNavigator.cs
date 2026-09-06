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

    /// <summary>
    /// 直前に取得した「レース選択」ページ（<see cref="JraRaceListPage"/>）そのもののキャッシュ。
    /// 同一開催（同一日付・競馬場・経路）に対して <see cref="ToRaceListAsync"/> /
    /// <see cref="ToRaceResultMeetingListAsync"/> がレースごとに繰り返し呼ばれる際、
    /// 従来はブラウザの「戻る」で復帰を
    /// 試みていたが、実サイトでは「戻る」後のページが期待するレース選択ページとして
    /// 認識できないケース（Kind=Unknownで開催選択ページに戻ってしまう等）が常態化しており、
    /// 毎回ブラウザの「戻る」試行（失敗）＋フルパス再遷移（1回あたり10秒以上）が発生し、
    /// 1開催日のレース数だけこのコストが積み重なって実行時間の大半を占めていた
    /// （本番ログで確認済み）。レース選択ページの内容は同一開催内では変わらないため、
    /// ブラウザを一切操作せずこのキャッシュを直接返せば十分であり、GoBackの試行自体が
    /// 不要になる。
    /// </summary>
    private (DateOnly Date, RaceCourse Course, string Route, JraRaceListPage Page)? _lastRaceListPage;

    /// <summary>
    /// 直前に取得したカレンダーページのキャッシュ（月, ページ）。
    /// <see cref="ToCalendarAsync"/> は日単位のジョブ登録処理（例:
    /// ScrapingRegistrationService のルックアヘッド日数分のループ）から同じ月に対して
    /// 何度も呼ばれ得るが、カレンダーページは月単位の内容であり日ごとに再取得する必要がない。
    /// 再取得はページ遷移・スナップショット抽出だけで1回あたり1分近くかかることがあり、
    /// ルックアヘッド日数が多い場合にLambdaの内部デッドライン（9分）を使い切って
    /// サイクル全体が未処理のままキャンセルされる原因になっていたため、同一セッション内で
    /// 同じ月への呼び出しはキャッシュを再利用する。
    /// </summary>
    private (YearMonth Month, JraCalendarPage Page)? _lastCalendarPage;

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
        if (_lastCalendarPage is { } cached && cached.Month == month)
        {
            _logger.LogInformation(
                "JRA navigation skipped (cache hit). Destination=Calendar Month={Month}",
                month);
            return cached.Page;
        }

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

        if (page is JraCalendarPage calendarPage)
        {
            _lastCalendarPage = (month, calendarPage);
        }

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

        if (_lastRaceListPage is { } cached
            && cached.Date == date && cached.Course == course && cached.Route == "Card")
        {
            // GoBack（ブラウザ履歴の「戻る」）は実サイトで信頼できず、開催選択ページに
            // 着地してしまう事象が常態化していた。既知のURLへ直接ナビゲートする方が
            // 確実で、かつカレンダー再確認・トップ再訪問・メニュー/開催選択ボタンの
            // クリックといった重い手順を省略できる。レース番号リンクのクリック
            // （NavigateRaceNumberLinkAsync）にはブラウザが実際にこのページ上にいる
            // 必要があるため、データだけ返すのではなく実際にナビゲートしてから返す。
            await _browser.NavigateAsync(cached.Page.Url, cancellationToken);
            var revisitedPage = await _pageReader.ReadAsync(cancellationToken);
            _logger.LogInformation(
                "JRA navigation done. Destination=RaceList Route=CachedUrl Date={Date} Course={Course} Url={Url}",
                date,
                course,
                revisitedPage.Url);
            return revisitedPage;
        }

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

        if (page is JraRaceListPage cardRaceList && cardRaceList.Date == date && cardRaceList.Course == course)
        {
            _lastRaceListPage = (date, course, "Card", cardRaceList);
        }

        _logger.LogInformation(
            "JRA navigation done. Destination=RaceList Route=Full ResolvedKind={Kind} Url={Url}",
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
            try
            {
                route = "Recent";
                page = await ToRecentRaceResultAsync(race, cancellationToken);
            }
            catch (JraNavigationException ex)
                when (ex.Reason == JraNavigationFailureReason.OutOfDisplayedRange)
            {
                // 「最近の過去開催」しきい値（92日、暫定値）は実際のページ掲載範囲
                // （今週～直近数週間程度）より広い可能性があり、しきい値内でも
                // 開催選択ページに対象日が載っていないことがある（Task16調査で
                // 判明した未検証事項）。その場合は「過去レース結果検索」フォーム
                // 経由の導線へフォールバックする。
                _logger.LogInformation(
                    ex,
                    "JRA navigation fallback. Recent route out of displayed range for Race={Race}. Falling back to Historical route.",
                    race);

                route = "HistoricalFallback";
                page = await ToHistoricalRaceResultAsync(race, cancellationToken);
            }
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

    /// <summary>
    /// 対象日・競馬場の「レース結果 レース選択」（またはそれに相当する）ページを取得する。
    /// <see cref="ToRaceResultAsync"/> は対象Rの成績ページまで遷移するのに対し、こちらは
    /// 対象日・競馬場のレース一覧の特定のみを目的とする（成績収集ジョブが「その日の
    /// 全レース番号を知る」ために使う）。
    ///
    /// 出馬表側の <see cref="ToRaceListAsync"/> は「出馬表 開催選択」ページ
    /// （/JRADB/accessD.html、今週～直近数週間程度しか掲載されない）にしか対応しておらず、
    /// 過去月をまたぐ日付を渡すと <see cref="JraNavigationException"/> になる
    /// （出馬表は公開期間を過ぎると通常ページ自体が消えるため、これは仕様として妥当）。
    /// レース結果の収集では同じ制約を負う必要がないため、<see cref="ToRaceResultAsync"/>
    /// と同じ Current/Recent/Historical のルート分岐を持つ本メソッドを別途用意した。
    /// </summary>
    public async Task<IJraPage> ToRaceResultListAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "JRA navigation start. Destination=RaceResultList Date={Date} Course={Course} CurrentUrl={CurrentUrl}",
            date,
            course,
            _browser.CurrentUrl);

        IJraPage page;
        string route;

        if (IsCurrentRacePeriod(date))
        {
            route = "Current";
            page = await ToRaceResultMeetingListAsync(date, course, cancellationToken);
        }
        else if (IsRecentRacePeriod(date))
        {
            try
            {
                route = "Recent";
                page = await ToRaceResultMeetingListAsync(date, course, cancellationToken);
            }
            catch (JraNavigationException ex)
                when (ex.Reason == JraNavigationFailureReason.OutOfDisplayedRange)
            {
                _logger.LogInformation(
                    ex,
                    "JRA navigation fallback. Recent route out of displayed range for Date={Date} Course={Course}. Falling back to Historical route.",
                    date,
                    course);

                route = "HistoricalFallback";
                page = await ToHistoricalRaceResultListAsync(date, course, cancellationToken);
            }
        }
        else
        {
            route = "Historical";
            page = await ToHistoricalRaceResultListAsync(date, course, cancellationToken);
        }

        _logger.LogInformation(
            "JRA navigation done. Destination=RaceResultList Route={Route} ResolvedKind={Kind} Url={Url}",
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

    private const string ResultRoute = "Result";

    private Task<IJraPage> ToCurrentRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken)
        // Current/Recentは実サイト上同一ページ・同一遷移に帰着するため
        // （Task16実サイト確認で判明）、共通実装を呼び出す。
        => ToRaceResultViaMeetingSelectionAsync(race, cancellationToken);

    private Task<IJraPage> ToRecentRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken)
        // 実サイト確認の結果、「過去のレース結果」はページ内の見出しであり
        // クリック可能なリンクではない。現在開催・直近開催とも同一の
        // 「レース結果 開催選択」ページに開催ボタンが並ぶため、Currentと全く同じ
        // 遷移で到達できる（Task16実サイト確認で判明）。
        => ToRaceResultViaMeetingSelectionAsync(race, cancellationToken);

    /// <summary>
    /// 「レース結果 開催選択」ページ経由で対象Rへ遷移する。同一開催に対して
    /// 連続で呼ばれる場合は、直前に取得した「レース選択」ページのキャッシュ
    /// （<see cref="_lastRaceListPage"/>）を再利用し、競馬トップ再訪問・「レース結果」
    /// メニュークリック・開催選択ボタンクリックを省略する。
    /// </summary>
    private async Task<IJraPage> ToRaceResultViaMeetingSelectionAsync(
        RaceId race,
        CancellationToken cancellationToken)
    {
        await ToRaceResultMeetingListAsync(
            race.Date,
            race.Course,
            cancellationToken);

        await NavigateRaceNumberLinkAsync(
            race.Number,
            JraNavigationLinks.RaceResult,
            cancellationToken);

        return await _pageReader.ReadAsync(cancellationToken);
    }

    /// <summary>
    /// 「レース結果 開催選択」ページ経由で対象日・競馬場の「レース選択」ページへ
    /// 遷移する（対象R番号の特定は行わない）。同一開催に対して連続で呼ばれる場合は、
    /// 直前に取得した「レース選択」ページのキャッシュ（<see cref="_lastRaceListPage"/>）を
    /// 再利用する。
    /// 対象日が開催選択ページの表示範囲外であれば <see cref="JraNavigationException"/>
    /// （<see cref="JraNavigationFailureReason.OutOfDisplayedRange"/> または
    /// <see cref="JraNavigationFailureReason.NotYetPublished"/>）を送出する。
    /// </summary>
    private async Task<IJraPage> ToRaceResultMeetingListAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken)
    {
        if (_lastRaceListPage is { } cached
            && cached.Date == date && cached.Course == course && cached.Route == ResultRoute)
        {
            // 出馬表側と同じ理由（GoBackの信頼性の問題）で、既知のURLへ直接ナビゲートする。
            await _browser.NavigateAsync(cached.Page.Url, cancellationToken);
            var revisitedPage = await _pageReader.ReadAsync(cancellationToken);
            _logger.LogInformation(
                "JRA navigation done. Destination=RaceResultList Route=CachedUrl Date={Date} Course={Course} Url={Url}",
                date,
                course,
                revisitedPage.Url);
            return revisitedPage;
        }

        // 競馬トップ → レース結果 → 対象日・競馬場（開催選択ボタン）
        await NavigateToRaceResultTopAsync(cancellationToken);

        await ClickMeetingButtonAsync(
            date,
            course,
            cancellationToken);

        var listPage =
            await _pageReader.ReadAsync(cancellationToken);

        if (listPage is JraRaceListPage raceList &&
            raceList.Date == date &&
            raceList.Course == course)
        {
            _lastRaceListPage = (date, course, ResultRoute, raceList);
        }

        return listPage;
    }

    private async Task<IJraPage> ToHistoricalRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken)
    {
        var afterMeeting =
            await ToHistoricalRaceResultListAsync(
                race.Date,
                race.Course,
                cancellationToken);

        // 重賞レースは開催選択ボタンから直接「レース結果」ページへリンクされている
        // ため、そのページが既に「レース結果」であればそのまま返す。そうでなければ
        // 「レース結果 レース選択」ページとみなし、対象R番号のリンクを辿る。
        if (afterMeeting.Kind == JraPageKind.RaceResult)
        {
            return afterMeeting;
        }

        await NavigateRaceNumberLinkAsync(
            race.Number,
            JraNavigationLinks.RaceResult,
            cancellationToken);

        return await _pageReader.ReadAsync(cancellationToken);
    }

    /// <summary>
    /// 「過去レース結果検索」フォーム経由で対象日・競馬場の開催選択を行い、
    /// その結果ページ（「レース結果 レース選択」ページ、または重賞レースの場合は
    /// 直接「レース結果」ページ）を返す。
    /// </summary>
    private async Task<IJraPage> ToHistoricalRaceResultListAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken)
    {
        // 競馬トップ → レース結果 → 過去レース結果検索 → 年月選択 → 検索実行
        // → 開催選択（当該日・競馬場のボタン、または重賞レースなら直リンク）。
        //
        // Task 13 実サイト調査で判明した「過去レース結果検索」ページの実構造:
        // 開催年(id=kaisaiY_list)・開催月(id=kaisaiM_list)は<form>タグの外にある
        // 素のselectで、「検索」も<a href="#" onclick="getSelectData();">という
        // JSリンクでありsubmitボタンではない。ページ内に実在する<form>
        // (commForm01)はJSのdoAction()が裏方でcnameを注入してPOSTするための
        // 隠しフォームに過ぎず、通常のフォーム入力の対象にはなり得ない。
        // そのため、ここではドキュメント記載のIWebBrowserメソッドのうち
        // SelectOptionAsync（年・月のselect操作）を用いる。
        // GetFormsAsync/SetFieldValueAsync/SubmitFormAsyncは、この経路が
        // 実サイトの通常フォームと噛み合わないため使用しない。
        //
        // 「検索」リンクは、ページヘッダーの検索ウィンドウ内にも同じ表示テキスト
        // 「検索」を持つ要素が存在し、単純な ClickAsync("検索") では誤って
        // ヘッダー側の要素を選んでしまうことがある（実サイトE2E再検証で判明）。
        // 見出し「開催年月」を含むブロック(div.layout_grid)内の「検索」を
        // クリックする ClickActionInSectionAsync で一意に特定する。
        // ハング調査用の段階別タイミングログ（手動E2Eで観測されたCPU使用率0%の
        // 無進行事象の原因特定のため、各ブラウザ操作の所要時間を計測して残す。
        // 恒久的な最適化のためのものではなく、原因が絞り込めた段階で削除・簡略化してよい。
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await ToHistoricalRaceSearchAsync(cancellationToken);
        LogDiagStep("ToHistoricalRaceSearchAsync", date, course, stopwatch);

        await _browser.SelectOptionAsync(
            "年",
            date.Year.ToString(),
            cancellationToken);
        LogDiagStep("SelectOptionAsync(年)", date, course, stopwatch);

        await _browser.SelectOptionAsync(
            "月",
            date.Month.ToString(),
            cancellationToken);
        LogDiagStep("SelectOptionAsync(月)", date, course, stopwatch);

        await _browser.ClickActionInSectionAsync(
            "開催年月",
            "検索",
            cancellationToken);
        LogDiagStep("ClickActionInSectionAsync(検索)", date, course, stopwatch);

        await ClickMeetingButtonAsync(
            date,
            course,
            cancellationToken);
        LogDiagStep("ClickMeetingButtonAsync", date, course, stopwatch);

        var page = await _pageReader.ReadAsync(cancellationToken);
        LogDiagStep("PageReader.ReadAsync", date, course, stopwatch);

        return page;
    }

    private void LogDiagStep(
        string step,
        DateOnly date,
        RaceCourse course,
        System.Diagnostics.Stopwatch stopwatch)
    {
        _logger.LogInformation(
            "[Diag] HistoricalRaceResultList step done. Step={Step} Date={Date} Course={Course} ElapsedMs={ElapsedMs}",
            step,
            date,
            course,
            stopwatch.ElapsedMilliseconds);
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

        // ハング調査用: GetPageSnapshotAsync自体がブラウザ側の要素走査を伴う重い
        // 処理であるため、開催選択ページの候補件数が多い場合にここで時間がかかって
        // いないかを切り分けられるよう、所要時間を計測してログに残す。
        var snapshotStopwatch =
            System.Diagnostics.Stopwatch.StartNew();

        var snapshot =
            await _browser.GetPageSnapshotAsync(
                cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[Diag] GetPageSnapshotAsync done. Date={Date} Course={Course} ElapsedMs={ElapsedMs} SectionCount={SectionCount}",
            date,
            course,
            snapshotStopwatch.ElapsedMilliseconds,
            snapshot.Sections.Count);

        var buttonText =
            FindMeetingButtonText(snapshot.MainText, date, courseName);

        if (buttonText is null)
        {
            // 対象日が今日より未来であれば「まだ公開されていない」、今日以前であれば
            // 「開催選択ページの表示範囲（今週～直近数週間程度）を外れた過去日」と推定する。
            // どちらも同じ「ボタンが見つからない」現象として観測されるが、呼び出し元での
            // 扱い（未公開ならリトライ、範囲外なら別導線へフォールバック/エラー記録）が
            // 異なるため、理由を区別して例外に含める。
            var reason =
                date > _today()
                    ? JraNavigationFailureReason.NotYetPublished
                    : JraNavigationFailureReason.OutOfDisplayedRange;

            var reasonText =
                reason == JraNavigationFailureReason.NotYetPublished
                    ? "対象日が未来のため、まだ開催情報が公開されていない可能性があります。"
                    : "対象日が開催選択ページの表示範囲（直近数週間程度）を外れている可能性があります。";

            throw new JraNavigationException(
                $"{date:yyyy-MM-dd} {course} の開催選択ボタンが見つかりませんでした。{reasonText}",
                reason);
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

    private static readonly Regex SchemePrefixRegex = new(
        @"^[a-zA-Z][a-zA-Z0-9+.\-]*:",
        RegexOptions.Compiled);

    internal static string? ResolveUrl(
        string? currentUrl,
        string href)
    {
        // Uri.TryCreate(href, UriKind.Absolute, ...) は "/keiba/calendar/" のような
        // 相対パスであっても、環境によっては file:// スキームの絶対URIとして誤判定
        // してしまうことがある。href が実際にスキーム（"https:" 等）を持つ場合のみ
        // 絶対URIとして扱い、それ以外は常に currentUrl を基準に相対解決する。
        if (SchemePrefixRegex.IsMatch(href) &&
            Uri.TryCreate(
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
