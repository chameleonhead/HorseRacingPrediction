# JRAスクレイピング層 実装指示書

## 1. 目的

既存の `IWebBrowser` / `PlaywrightWebBrowser` の上に、JRAサイト専用のスクレイピング層を実装する。

今回の設計では以下を明確に分離する。

```text
PlaywrightWebBrowser
    ↓
汎用ブラウザー操作・汎用PageSnapshot取得

JraNavigator
    ↓
JRAサイト内のページ遷移

JraPageReader
    ↓
現在ページのPageSnapshot取得・種類判定・解析

IJraPage
    ↓
解析済みのJRAページ状態
```

重要な設計原則は次の通り。

```text
JraSession.Navigate
    = ブラウザーを操作する

IJraPage
    = 取得済みページの状態を表す

JraPageParser
    = PageSnapshotをIJraPageへ変換する
```

`IJraPage` 自体にはブラウザー操作を持たせない。

例えば以下のようなAPIを目標とする。

```csharp
await using var browser =
    await PlaywrightWebBrowser.CreateAsync();

var session =
    new JraSession(
        browser,
        navigator,
        pageReader);

IJraPage page =
    await session.Navigate.ToCalendarAsync(
        new YearMonth(2026, 9));

if (page is JraCalendarPage calendar)
{
    foreach (var raceDate in calendar.RaceDates)
    {
        Console.WriteLine(raceDate.Date);
    }
}
```

さらに、

```csharp
page =
    await session.Navigate.ToRaceListAsync(
        new DateOnly(2026, 9, 6),
        RaceCourse.Chukyo);

if (page is JraRaceListPage raceList)
{
    var race =
        raceList.Races.Single(x => x.Number == 11);

    page =
        await session.Navigate.ToRaceCardAsync(race.Id);
}
```

のように、現在ブラウザーがどのページにいるかを呼び出し側が意識しなくてもよい構造にする。

---

# 2. 既存コードについて

以下は既存実装として維持する。

```text
HorseRacingPrediction.Scraping.Browser
```

配下の、

```csharp
IWebBrowser
PlaywrightWebBrowser

PageSnapshot
PageSectionSnapshot
PageLinkSnapshot
PageTableSnapshot
PageFormSnapshot
PageImageSnapshot
PageActionSnapshot
```

など。

`PlaywrightWebBrowser` にJRA固有の処理は追加しない。

例えば以下は禁止する。

```csharp
PlaywrightWebBrowser.OpenJraCalendarAsync(...)
PlaywrightWebBrowser.FindRaceAsync(...)
PlaywrightWebBrowser.GetRaceCardAsync(...)
```

`PlaywrightWebBrowser` は今後も汎用Webブラウザーとして維持する。

---

# 3. 新規ディレクトリ構成

以下を基本構成とする。

```text
HorseRacingPrediction.Scraping/
│
├─ Browser/
│   └─ 既存実装
│
└─ Jra/
    │
    ├─ JraSession.cs
    │
    ├─ JraUrls.cs
    │
    │
    ├─ Models/
    │   ├─ YearMonth.cs
    │   ├─ RaceCourse.cs
    │   ├─ RaceId.cs
    │   ├─ JraRaceDate.cs
    │   ├─ RaceSummary.cs
    │   ├─ RaceCard.cs
    │   ├─ RaceEntry.cs
    │   ├─ RaceResult.cs
    │   └─ RaceResultEntry.cs
    │
    ├─ Pages/
    │   ├─ IJraPage.cs
    │   ├─ JraPageKind.cs
    │   ├─ JraUnknownPage.cs
    │   ├─ JraKeibaTopPage.cs
    │   ├─ JraCalendarPage.cs
    │   ├─ JraRaceListPage.cs
    │   ├─ JraRaceCardPage.cs
    │   ├─ JraRaceResultPage.cs
    │   ├─ JraRecentRaceResultsPage.cs
    │   └─ JraHistoricalRaceSearchPage.cs
    │
    ├─ Parsing/
    │   ├─ IJraPageParser.cs
    │   ├─ JraPageReader.cs
    │   ├─ JraPageParserRegistry.cs
    │   ├─ CalendarPageParser.cs
    │   ├─ RaceListPageParser.cs
    │   ├─ RaceCardPageParser.cs
    │   ├─ RaceResultPageParser.cs
    │   ├─ RecentRaceResultsPageParser.cs
    │   └─ HistoricalRaceSearchPageParser.cs
    │
    └─ Navigation/
        ├─ IJraNavigator.cs
        ├─ JraNavigator.cs
        ├─ JraDestination.cs
        ├─ JraNavigationException.cs
        └─ JraNavigationLinks.cs
```

最初の実装では、クラス数削減のために `JraPageParserRegistry` を省略して `JraPageReader` に parser collection を直接注入してもよい。

ただし `JraPageReader` 内にページごとの解析コードそのものを書かないこと。

---

# 4. Domainモデル

## 4.1 YearMonth

.NET標準にYearMonth型がないため、簡単なValue Objectを作成する。

```csharp
namespace HorseRacingPrediction.Scraping.Jra.Models;

public readonly record struct YearMonth(
    int Year,
    int Month)
{
    public YearMonth
    {
        if (Year is < 1900 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(Year));
        }

        if (Month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(Month));
        }
    }

    public DateOnly FirstDay
        => new(Year, Month, 1);

    public override string ToString()
        => $"{Year:D4}-{Month:D2}";
}
```

1900/2200の範囲は厳密なJRA仕様ではなく、異常値防止用。

必要なら後から範囲を変更してよい。

---

# 5. RaceCourse

```csharp
namespace HorseRacingPrediction.Scraping.Jra.Models;

public enum RaceCourse
{
    Unknown = 0,

    Sapporo,
    Hakodate,
    Fukushima,
    Niigata,
    Tokyo,
    Nakayama,
    Chukyo,
    Kyoto,
    Hanshin,
    Kokura
}
```

日本語名との変換はenum内部では行わない。

必要に応じて別クラスへ分離する。

```csharp
internal static class RaceCourseNames
{
    public static RaceCourse Parse(string text)
    {
        if (text.Contains("札幌"))
            return RaceCourse.Sapporo;

        if (text.Contains("函館"))
            return RaceCourse.Hakodate;

        if (text.Contains("福島"))
            return RaceCourse.Fukushima;

        if (text.Contains("新潟"))
            return RaceCourse.Niigata;

        if (text.Contains("東京"))
            return RaceCourse.Tokyo;

        if (text.Contains("中山"))
            return RaceCourse.Nakayama;

        if (text.Contains("中京"))
            return RaceCourse.Chukyo;

        if (text.Contains("京都"))
            return RaceCourse.Kyoto;

        if (text.Contains("阪神"))
            return RaceCourse.Hanshin;

        if (text.Contains("小倉"))
            return RaceCourse.Kokura;

        return RaceCourse.Unknown;
    }
}
```

この実装は初期版。

将来的に完全一致やJRAコードを利用できるなら変更してよい。

---

# 6. RaceId

アプリケーション側では、

```text
日付
競馬場
レース番号
```

でレースを識別する。

```csharp
public sealed record RaceId(
    DateOnly Date,
    RaceCourse Course,
    int Number)
{
    public RaceId
    {
        if (Number is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(Number));
        }
    }
}
```

JRA内部の開催回・開催日番号等を後から取得できる場合でも、ここへ混ぜない。

必要になったら別途、

```csharp
JraRaceKey
```

を追加する。

---

# 7. カレンダーページモデル

カレンダーから取得するのは「開催Meeting」ではなく、開催日と開催場所。

```csharp
public sealed record JraRaceDate(
    DateOnly Date,
    IReadOnlyList<RaceCourse> Courses);
```

ページモデル。

```csharp
public sealed record JraCalendarPage(
    string Url,
    YearMonth Month,
    IReadOnlyList<JraRaceDate> RaceDates)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.Calendar;
}
```

例えば、

```text
2026/09/05
    札幌
    中山
    阪神
```

が表示されていれば、

```csharp
new JraRaceDate(
    new DateOnly(2026, 9, 5),
    [
        RaceCourse.Sapporo,
        RaceCourse.Nakayama,
        RaceCourse.Hanshin,
    ]);
```

のようになる。

---

# 8. RaceSummary

レース一覧ページから取得できる最低限の情報。

```csharp
public sealed record RaceSummary(
    RaceId Id,
    string? Name,
    TimeOnly? StartTime,
    string? RaceCardUrl,
    string? ResultUrl)
{
    public int Number => Id.Number;
}
```

初期実装で取れない値は `null` でよい。

DOM解析の都合で推測して値を入れないこと。

---

# 9. IJraPage

```csharp
namespace HorseRacingPrediction.Scraping.Jra.Pages;

public interface IJraPage
{
    JraPageKind Kind { get; }

    string Url { get; }
}
```

種類。

```csharp
public enum JraPageKind
{
    Unknown = 0,

    KeibaTop,

    Calendar,

    RaceList,

    RaceCard,

    RaceResult,

    RecentRaceResults,

    HistoricalRaceSearch
}
```

初期実装ではこの程度でよい。

ページが増えた時だけ追加する。

---

# 10. その他のページモデル

## JraRaceListPage

```csharp
public sealed record JraRaceListPage(
    string Url,
    DateOnly Date,
    RaceCourse Course,
    IReadOnlyList<RaceSummary> Races)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.RaceList;
}
```

## JraRaceCardPage

```csharp
public sealed record JraRaceCardPage(
    string Url,
    RaceId RaceId,
    string? RaceName,
    TimeOnly? StartTime,
    IReadOnlyList<RaceEntry> Entries)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.RaceCard;
}
```

`RaceEntry` はまず必要最低限でよい。

```csharp
public sealed record RaceEntry(
    int HorseNumber,
    string HorseName,
    int? FrameNumber,
    string? JockeyName,
    decimal? Weight);
```

ここでの `Weight` が馬体重なのか斤量なのか曖昧なため、実際のJRA項目に合わせて命名を調整すること。

例えば斤量なら、

```csharp
decimal? AssignedWeight
```

のようにする。

---

## JraRaceResultPage

```csharp
public sealed record JraRaceResultPage(
    string Url,
    RaceId RaceId,
    string? RaceName,
    IReadOnlyList<RaceResultEntry> Results)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.RaceResult;
}
```

```csharp
public sealed record RaceResultEntry(
    int FinishPosition,
    int HorseNumber,
    string HorseName,
    string? JockeyName,
    TimeSpan? Time);
```

これも初期モデル。

実際に必要な、

```text
着差
単勝人気
オッズ
上がり
馬体重
調教師
賞金
```

などはページ解析時に確認して追加する。

今回の指示では全項目定義までは行わない。

---

# 11. Unknownページ

想定外ページを例外にせず取得できるようにする。

```csharp
public sealed record JraUnknownPage(
    string Url,
    string Title)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.Unknown;
}
```

スクレイパーの場合、

```text
想定ページでなければ即例外
```

だけにすると、JRA側の軽微な変更時に調査しにくくなる。

そのためReader自体はUnknownを返せるようにする。

ただし、

```csharp
Navigate.ToCalendarAsync(...)
```

のように目的地が明確な場合、戻ってきたページがUnknownならNavigation側で例外化してもよい。

初期実装では `IJraPage` をそのまま返す。

---

# 12. Page Parser

ページごとの解析責務を分離する。

```csharp
public interface IJraPageParser
{
    JraPageKind Kind { get; }

    int Priority { get; }

    bool CanParse(PageSnapshot snapshot);

    IJraPage Parse(PageSnapshot snapshot);
}
```

Parse内で追加ブラウザー操作はしない。

つまり、

```csharp
Browser.ClickAsync(...)
Browser.NavigateAsync(...)
```

は禁止。

Parserは純粋に、

```text
PageSnapshot
    ↓
IJraPage
```

へ変換する。

可能な限り同期処理にする。

---

# 13. CalendarPageParser

概念実装。

```csharp
internal sealed class CalendarPageParser
    : IJraPageParser
{
    public JraPageKind Kind =>
        JraPageKind.Calendar;

    public int Priority => 100;

    public bool CanParse(
        PageSnapshot snapshot)
    {
        if (snapshot.Url.Contains(
                "/keiba/calendar/",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return snapshot.Sections.Any(section =>
            section.Headings.Any(heading =>
                heading.Contains(
                    "開催日程",
                    StringComparison.Ordinal)));
    }

    public IJraPage Parse(
        PageSnapshot snapshot)
    {
        var month =
            ParseMonth(snapshot);

        var dates =
            ParseRaceDates(snapshot, month);

        return new JraCalendarPage(
            snapshot.Url,
            month,
            dates);
    }
}
```

`ParseMonth` はURL、タイトル、見出し等から取得する。

概念例。

```csharp
private static YearMonth ParseMonth(
    PageSnapshot snapshot)
{
    var match = Regex.Match(
        $"{snapshot.Title} {string.Join(" ", snapshot.Sections.SelectMany(x => x.Headings))}",
        @"(?<year>\d{4})年\s*(?<month>\d{1,2})月");

    if (!match.Success)
    {
        throw new JraPageParseException(
            JraPageKind.Calendar,
            snapshot.Url,
            "対象年月を取得できませんでした。");
    }

    return new YearMonth(
        int.Parse(match.Groups["year"].Value),
        int.Parse(match.Groups["month"].Value));
}
```

ここはJRA実ページ構造確認後に具体化する。

現時点では完全実装しない。

---

# 14. Calendar開催日解析

まずは `PageSnapshot.Sections` 内のテキスト・リンクから解析する。

重要なのはHTML selectorをJRA層へ追加するのではなく、既存 `PageSnapshot` で取得可能な範囲を優先すること。

概念例。

```csharp
private static IReadOnlyList<JraRaceDate>
    ParseRaceDates(
        PageSnapshot snapshot,
        YearMonth month)
{
    var results =
        new Dictionary<
            DateOnly,
            HashSet<RaceCourse>>();

    foreach (var section in snapshot.Sections)
    {
        ParseText(
            section.MainText,
            month,
            results);

        foreach (var link in section.Links)
        {
            ParseText(
                $"{link.Title} {link.Url}",
                month,
                results);
        }
    }

    return results
        .OrderBy(x => x.Key)
        .Select(x =>
            new JraRaceDate(
                x.Key,
                x.Value.ToArray()))
        .ToArray();
}
```

`ParseText` の正規表現や具体ロジックはJRA実ページ調査後に作る。

この部分はこの指示書では意図的に割愛している。

---

# 15. JraPageReader

現在ブラウザーに表示されているページを解析する唯一の入口。

```csharp
public sealed class JraPageReader
{
    private readonly IWebBrowser _browser;

    private readonly IReadOnlyList<IJraPageParser>
        _parsers;

    public JraPageReader(
        IWebBrowser browser,
        IEnumerable<IJraPageParser> parsers)
    {
        _browser = browser;

        _parsers = parsers
            .OrderByDescending(x => x.Priority)
            .ToArray();
    }

    public async Task<IJraPage> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot =
            await _browser.GetPageSnapshotAsync(
                cancellationToken: cancellationToken);

        foreach (var parser in _parsers)
        {
            if (parser.CanParse(snapshot))
            {
                return parser.Parse(snapshot);
            }
        }

        return new JraUnknownPage(
            snapshot.Url,
            snapshot.Title);
    }
}
```

このクラスは非常に薄く保つ。

---

# 16. JraSession

```csharp
public sealed class JraSession
{
    public JraSession(
        IJraNavigator navigator,
        JraPageReader pageReader)
    {
        Navigate = navigator;
        Pages = pageReader;
    }

    public IJraNavigator Navigate { get; }

    public JraPageReader Pages { get; }
}
```

利用側は必要なら、

```csharp
var page =
    await session.Pages.ReadAsync();
```

で現在ページを解析できる。

ただし通常は、

```csharp
await session.Navigate.ToCalendarAsync(...)
```

などを使用する。

---

# 17. Navigator interface

```csharp
public interface IJraNavigator
{
    Task<IJraPage> ToKeibaTopAsync(
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToCalendarAsync(
        YearMonth month,
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToRaceListAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToRaceCardAsync(
        RaceId race,
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToHistoricalRaceSearchAsync(
        CancellationToken cancellationToken = default);
}
```

公開APIでは、

```text
現在ページ
JRA内部URL
開催回番号
```

等を呼び出し側へ要求しない。

---

# 18. JraUrls

固定URLは集中管理する。

```csharp
internal static class JraUrls
{
    public const string KeibaTop =
        "https://www.jra.go.jp/keiba/";

    public const string Calendar =
        "https://www.jra.go.jp/keiba/calendar/";
}
```

可能ならURL直打ちは最小限にする。

固定入口としてのみ利用する。

実際のレースページURLはJRAサイト上のリンクから取得する。

---

# 19. Navigatorの基本戦略

どのページからでもリンク経由で目的ページへ遷移できるようにする。

基本アルゴリズムは以下。

```text
現在ページのリンクを取得
    ↓
目的リンクを探す
    ↓
見つかればhrefへNavigateAsync
    ↓
見つからなければ競馬トップへ移動
    ↓
再度目的リンクを探す
    ↓
移動
    ↓
JraPageReader.ReadAsync()
```

`ClickAsync` より `GetLinksAsync` + `NavigateAsync` を優先する。

理由は、

```text
リンク文字列の曖昧一致による誤クリックを避ける
相対URLを明示的に扱える
履歴やナビゲーションを追跡しやすい
```

ため。

---

# 20. JraNavigationLinks

リンク文字列を一箇所へ集約する。

```csharp
internal static class JraNavigationLinks
{
    public static readonly string[] Calendar =
    [
        "開催日程"
    ];

    public static readonly string[] RaceCard =
    [
        "出馬表"
    ];

    public static readonly string[] RaceResult =
    [
        "レース結果"
    ];

    public static readonly string[] HistoricalRaceSearch =
    [
        "過去レース結果検索"
    ];
}
```

JRA表記変更時の影響範囲をここへ限定する。

ただしレース番号等、ページ固有リンクまではここへ置かない。

---

# 21. JraNavigator基本実装

```csharp
public sealed class JraNavigator
    : IJraNavigator
{
    private readonly IWebBrowser _browser;
    private readonly JraPageReader _pageReader;

    public JraNavigator(
        IWebBrowser browser,
        JraPageReader pageReader)
    {
        _browser = browser;
        _pageReader = pageReader;
    }

    public async Task<IJraPage>
        ToKeibaTopAsync(
            CancellationToken cancellationToken = default)
    {
        await _browser.NavigateAsync(
            JraUrls.KeibaTop,
            cancellationToken);

        return await _pageReader.ReadAsync(
            cancellationToken);
    }

    public async Task<IJraPage>
        ToCalendarAsync(
            YearMonth month,
            CancellationToken cancellationToken = default)
    {
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

        return await _pageReader.ReadAsync(
            cancellationToken);
    }

    // 以下個別実装
}
```

---

# 22. 汎用リンク遷移

```csharp
private async Task<bool>
    TryNavigateByLinkAsync(
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
            ResolveUrl(
                _browser.CurrentUrl,
                link.Url);

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
```

`PageLinkSnapshot.Url` が絶対URLか相対URLかは既存実装を確認する。

現在の `CreateLinkAsync` では `href` がそのまま格納されているため、JRA層側で相対URL解決が必要になる可能性が高い。

共通化する。

```csharp
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
        Uri.TryCreate(
            currentUrl,
            UriKind.Absolute,
            out var current) &&
        Uri.TryCreate(
            current,
            href,
            out var resolved))
    {
        return resolved.ToString();
    }

    return null;
}
```

後から `IWebBrowser.GetLinksAsync()` 自体が絶対URLを返す仕様へ変更する場合は削除可能。

---

# 23. カレンダー月選択

JRAの月ページがURL直接指定可能ならURLを優先する。

そうでなければ月リンクを取得して遷移する。

概念コード。

```csharp
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
        ResolveUrl(
            _browser.CurrentUrl,
            target.Url)
        ?? throw new JraNavigationException(
            $"URLを解決できません: {target.Url}");

    await _browser.NavigateAsync(
        url,
        cancellationToken);
}
```

年度を跨ぐケースはJRAサイトの実際の導線を確認して対応する。

この指示書では年度選択方法の詳細は割愛する。

---

# 24. RaceList遷移

`ToRaceListAsync` は、

```text
カレンダーへ移動
対象年月選択
対象日検索
対象競馬場リンク検索
対象ページへ移動
```

の順にする。

概念コード。

```csharp
public async Task<IJraPage>
    ToRaceListAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken = default)
{
    var calendarPage =
        await ToCalendarAsync(
            new YearMonth(
                date.Year,
                date.Month),
            cancellationToken);

    if (calendarPage is not JraCalendarPage calendar)
    {
        throw new JraNavigationException(
            $"カレンダーページを取得できませんでした。 Kind={calendarPage.Kind}");
    }

    var raceDate =
        calendar.RaceDates
            .FirstOrDefault(x =>
                x.Date == date);

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

    return await _pageReader.ReadAsync(
        cancellationToken);
}
```

`NavigateToRaceDateCourseAsync` の具体的なリンク検索方法はJRAカレンダーHTML構造を確認して実装する。

ここでは割愛。

可能なら `JraRaceDate` にURLを持たせる設計も検討する。

例えば、

```csharp
public sealed record JraRaceMeetingLink(
    RaceCourse Course,
    string Url);
```

として、

```csharp
public sealed record JraRaceDate(
    DateOnly Date,
    IReadOnlyList<JraRaceMeetingLink> Meetings);
```

とすると、RaceListへの遷移が大幅に単純化する。

実ページから競馬場単位URLを取得できるならこちらを優先する。

---

# 25. RaceCard遷移

`RaceId` がある場合、

```text
RaceListへ移動
RaceSummary取得
対象RaceSummaryを選択
RaceCardUrlがあれば直接移動
なければレース番号リンクから移動
```

とする。

```csharp
public async Task<IJraPage>
    ToRaceCardAsync(
        RaceId race,
        CancellationToken cancellationToken = default)
{
    var page =
        await ToRaceListAsync(
            race.Date,
            race.Course,
            cancellationToken);

    if (page is not JraRaceListPage raceList)
    {
        throw new JraNavigationException(
            $"レース一覧を取得できませんでした。 Kind={page.Kind}");
    }

    var summary =
        raceList.Races
            .SingleOrDefault(x =>
                x.Id == race);

    if (summary is null)
    {
        throw new JraNavigationException(
            $"{race} がレース一覧に存在しません。");
    }

    if (!string.IsNullOrWhiteSpace(
            summary.RaceCardUrl))
    {
        await NavigateResolvedAsync(
            summary.RaceCardUrl,
            cancellationToken);

        return await _pageReader.ReadAsync(
            cancellationToken);
    }

    await NavigateRaceNumberLinkAsync(
        race.Number,
        cancellationToken);

    return await _pageReader.ReadAsync(
        cancellationToken);
}
```

---

# 26. RaceResult遷移

ここだけ、対象日時によってJRA側の入口が異なる。

外部APIは一本にする。

```csharp
Task<IJraPage> ToRaceResultAsync(
    RaceId race,
    CancellationToken cancellationToken = default);
```

内部は、

```text
現在開催週
最近の過去開催
古い開催
```

を判定する。

ただし最初からStrategyを大量に作らない。

まずは private method で分岐してよい。

```csharp
public async Task<IJraPage>
    ToRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken = default)
{
    if (IsCurrentRacePeriod(race.Date))
    {
        return await ToCurrentRaceResultAsync(
            race,
            cancellationToken);
    }

    if (IsRecentRacePeriod(race.Date))
    {
        return await ToRecentRaceResultAsync(
            race,
            cancellationToken);
    }

    return await ToHistoricalRaceResultAsync(
        race,
        cancellationToken);
}
```

期間判定を別クラスにする場合は、

```csharp
IRaceResultRoutePolicy
```

程度に留める。

Navigator Strategy化は、ルートが本当に複雑になった時点で行う。

---

# 27. 現在開催結果

概念フロー。

```text
競馬トップ
↓
レース結果
↓
対象日
↓
対象競馬場
↓
対象R
```

現在ページに「レース結果」リンクがあれば直接使う。

なければ競馬トップへ戻る。

---

# 28. 最近の過去結果

概念フロー。

```text
競馬トップ
↓
レース結果
↓
過去のレース結果
↓
対象開催
↓
対象レース
```

JRAの実際のリンク文言・ページ階層は実ページ確認後に固定する。

---

# 29. 古いレース結果

概念フロー。

```text
競馬トップ
↓
レース結果
↓
過去レース結果検索
↓
検索フォーム
↓
検索結果
↓
対象レース
```

検索フォーム操作が必要なので、

```csharp
IWebBrowser.SelectOptionAsync(...)
IWebBrowser.SetFieldValueAsync(...)
IWebBrowser.SubmitFormAsync(...)
```

を使用する。

ここはNavigator内に実装してよい。

`JraHistoricalRaceSearchPage` に操作メソッドを持たせない。

---

# 30. Page Parserのテスト方針

Parserはブラウザー不要でテストできるようにする。

```csharp
var snapshot =
    new PageSnapshot(
        url,
        title,
        sections);

var parser =
    new CalendarPageParser();

var page =
    parser.Parse(snapshot);

Assert.IsInstanceOfType<JraCalendarPage>(
    page);
```

ParserのテストではPlaywrightを起動しない。

これが今回の設計の大きな目的の一つ。

---

# 31. Navigationテスト方針

Navigationは `IWebBrowser` のFakeまたはMockでテストする。

最低限確認する。

```text
現在ページに目的リンクがあれば直接使う
目的リンクがなければ競馬トップへfallbackする
相対URLが正しく解決される
Calendarの年月を正しく切り替える
想定リンクがなければJraNavigationException
CancellationTokenを伝搬する
```

実JRAサイトを使ったE2Eは別テストプロジェクトへ分離する。

---

# 32. 例外

最低限以下を作成する。

```csharp
public sealed class JraNavigationException
    : Exception
{
    public JraNavigationException(
        string message)
        : base(message)
    {
    }

    public JraNavigationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
```

```csharp
public sealed class JraPageParseException
    : Exception
{
    public JraPageParseException(
        JraPageKind pageKind,
        string url,
        string message)
        : base(
            $"JRAページ解析に失敗しました。 " +
            $"Kind={pageKind}, Url={url}, Reason={message}")
    {
        PageKind = pageKind;
        Url = url;
    }

    public JraPageKind PageKind { get; }

    public string Url { get; }
}
```

DOMやページ内容を丸ごと例外メッセージへ入れないこと。

ログに必要なら長さ制限して記録する。

---

# 33. Logging

`JraNavigator` と `JraPageReader` に `ILogger<T>` を追加する。

例えば、

```csharp
_logger.LogInformation(
    "JRA navigation start. Destination={Destination} CurrentUrl={CurrentUrl}",
    "Calendar",
    _browser.CurrentUrl);
```

解析時。

```csharp
_logger.LogDebug(
    "JRA page detected. Kind={Kind} Url={Url}",
    page.Kind,
    page.Url);
```

Parser内部では通常Debug以下で十分。

---

# 34. DI

Microsoft.Extensions.DependencyInjectionを使用しているなら以下程度。

```csharp
services.AddSingleton<IWebBrowser>(...);

services.AddSingleton<IJraPageParser, CalendarPageParser>();
services.AddSingleton<IJraPageParser, RaceListPageParser>();
services.AddSingleton<IJraPageParser, RaceCardPageParser>();
services.AddSingleton<IJraPageParser, RaceResultPageParser>();
services.AddSingleton<IJraPageParser, RecentRaceResultsPageParser>();
services.AddSingleton<IJraPageParser, HistoricalRaceSearchPageParser>();

services.AddSingleton<JraPageReader>();
services.AddSingleton<IJraNavigator, JraNavigator>();
services.AddSingleton<JraSession>();
```

ただし `IWebBrowser` のライフサイクルがブラウザーセッション単位の場合、DIのSingleton固定が適切とは限らない。

既存アプリケーションのブラウザー生成方法を維持すること。

---

# 35. 実装タスク

## Task 1: JRA基本モデルを追加

対象:

```text
Jra/Models
Jra/Pages
```

実装する。

```text
YearMonth
RaceCourse
RaceId
JraRaceDate
RaceSummary

IJraPage
JraPageKind

JraCalendarPage
JraRaceListPage
JraRaceCardPage
JraRaceResultPage
JraUnknownPage
```

この時点ではRaceCard/Result詳細項目は最低限でよい。

完了条件:

```text
ビルド成功
モデル単体テスト成功
Playwright依存なし
```

---

## Task 2: JraPageParser基盤を追加

実装する。

```text
IJraPageParser
JraPageReader
JraPageParseException
```

まずParserは、

```text
Calendar
Unknown
```

だけでよい。

`JraPageReader.ReadAsync()` で、

```text
PageSnapshot
↓
CalendarPageParser
↓
JraCalendarPage
```

になることを確認する。

完了条件:

```text
Fake IWebBrowserでテスト可能
Playwright起動不要
Parser selection priorityがテストされている
Unknown fallbackがテストされている
```

---

## Task 3: CalendarPageParserを実装

実JRAカレンダーを調査して、

```text
対象年月
開催日
開催競馬場
```

を取得する。

このタスクではまだRaceListへ遷移しない。

入力:

```csharp
PageSnapshot
```

出力:

```csharp
JraCalendarPage
```

完了条件:

```csharp
page.Month
page.RaceDates
```

が取得できる。

例えば、

```csharp
Assert.IsTrue(
    page.RaceDates.Any());
```

だけではなく、

```text
特定日
特定開催場
```

を固定サンプルから検証する。

可能ならJRAページから取得したHTMLまたはPageSnapshot Fixtureをテストデータとして保存する。

ただしJRA利用規約・著作物の扱いに配慮し、大量HTMLをリポジトリに保存しない。

---

## Task 4: JraNavigator基盤を実装

実装する。

```text
IJraNavigator
JraNavigator
JraUrls
JraNavigationLinks
JraNavigationException
```

まず、

```csharp
ToKeibaTopAsync()
ToCalendarAsync(YearMonth)
```

だけ実装する。

`ToCalendarAsync` は、

```text
現在ページから開催日程リンク検索
↓
見つからなければCalendar固定URL
↓
対象月へ移動
↓
JraPageReader.ReadAsync()
↓
IJraPage返却
```

とする。

完了条件:

```csharp
IJraPage page =
    await navigator.ToCalendarAsync(
        new YearMonth(2026, 9));
```

が成立する。

Navigatorから `JraCalendarPage` を返さず、戻り値は `IJraPage` にする。

---

## Task 5: JraSessionを追加

実装。

```csharp
public sealed class JraSession
{
    public IJraNavigator Navigate { get; }

    public JraPageReader Pages { get; }
}
```

利用コードを書いたIntegration TestまたはSampleを追加する。

```csharp
var page =
    await session.Navigate.ToCalendarAsync(
        new YearMonth(2026, 9));

if (page is JraCalendarPage calendar)
{
    ...
}
```

完了条件:

アプリケーションコードから、

```text
IWebBrowser.ClickAsync
IWebBrowser.NavigateAsync
```

を直接使用せずカレンダーへ到達できる。

---

## Task 6: RaceListページ解析を実装

実JRAページを調査し、

```text
日付
競馬場
1Rから12R
レース名
発走時刻
出馬表リンク
結果リンク
```

の取得可能範囲を確認する。

`RaceListPageParser` を実装する。

```csharp
JraRaceListPage
```

を返す。

取得不能な項目はnull。

推測は禁止。

完了条件:

```csharp
raceList.Races[0].Id
raceList.Races[0].Number
```

が正しい。

リンクが取得可能なら、

```csharp
RaceCardUrl
ResultUrl
```

も格納する。

---

## Task 7: ToRaceListAsyncを実装

```csharp
Task<IJraPage> ToRaceListAsync(
    DateOnly date,
    RaceCourse course,
    CancellationToken cancellationToken = default);
```

を実装する。

基本フロー。

```text
ToCalendarAsync()
↓
JraCalendarPageから対象日確認
↓
対象競馬場確認
↓
対象リンクへ遷移
↓
JraPageReader.ReadAsync()
```

可能ならCalendar parserでURLまで取得し、

```text
テキストを再検索
```

する必要を減らす。

完了条件:

```csharp
var page =
    await session.Navigate.ToRaceListAsync(
        date,
        RaceCourse.Tokyo);

Assert.IsInstanceOfType<JraRaceListPage>(
    page);
```

---

## Task 8: RaceCardParserを実装

JRA出馬表から、

```text
RaceId
レース名
発走時刻
馬番
枠番
馬名
騎手
斤量
```

を最低限取得する。

必要なデータモデルをこのタスクで拡張する。

例えば、

```csharp
public sealed record RaceEntry(
    int HorseNumber,
    int? FrameNumber,
    string HorseName,
    string? JockeyName,
    decimal? AssignedWeight);
```

HTML上の実際の単位・項目を確認して命名する。

完了条件:

固定レースFixtureから、

```text
出走頭数
馬番
馬名
```

が正しく取得できる。

---

## Task 9: ToRaceCardAsyncを実装

```csharp
Task<IJraPage> ToRaceCardAsync(
    RaceId race,
    CancellationToken cancellationToken = default);
```

実装。

基本フロー。

```text
ToRaceListAsync
↓
RaceSummary検索
↓
RaceCardUrlがあればNavigateAsync
↓
なければレース番号リンク
↓
ReadAsync
```

完了条件:

現在ブラウザー位置に依存せず、

```csharp
await session.Navigate.ToRaceCardAsync(raceId);
```

だけで出馬表に到達する。

---

## Task 10: RaceResultParserを実装

JRA結果ページから最低限、

```text
RaceId
レース名
着順
馬番
馬名
騎手
走破タイム
```

を取得する。

結果モデルはこのタスクで必要に応じて拡張。

完了条件:

既知の完了レースFixtureから、

```text
1着馬
2着馬
3着馬
```

が一致する。

---

## Task 11: Current Race Result遷移

現在週・開催直後のレース結果導線を実装。

外部APIは、

```csharp
ToRaceResultAsync(RaceId)
```

のまま。

内部private methodとして、

```csharp
ToCurrentRaceResultAsync(...)
```

を追加してよい。

完了条件:

現在開催に近い結果へ到達できる。

---

## Task 12: Recent Race Result遷移

「過去のレース結果」導線を実装。

実ページを調査して、

```text
何日前
何ヶ月前
```

までこの導線を使用するのか確認する。

期間をマジックナンバーとしてNavigatorへ直接書かない。

例えば、

```csharp
private static readonly TimeSpan RecentResultPeriod =
    TimeSpan.FromDays(...);
```

よりも、後々ルール変更しやすいprivate policy methodを使う。

```csharp
private bool IsRecentRacePeriod(
    DateOnly raceDate)
```

完了条件:

現在週ではない最近のレース結果を取得できる。

---

## Task 13: Historical Race Search遷移

古い結果について、

```text
過去レース結果検索
```

を利用する。

既存 `IWebBrowser` の、

```csharp
GetFormsAsync
SelectOptionAsync
SetFieldValueAsync
SubmitFormAsync
```

を使用する。

JRAページオブジェクトにブラウザー操作を追加しない。

完了条件:

十分古いRaceIdから対象結果ページまで到達できる。

---

## Task 14: ToRaceResultAsync統合

現在・最近・古い検索を、

```csharp
ToRaceResultAsync(RaceId)
```

へ統合する。

呼び出し側はルートを意識しない。

```csharp
var resultPage =
    await session.Navigate.ToRaceResultAsync(
        raceId);
```

のみ。

完了条件:

3種類の期間を同一APIから取得できる。

---

## Task 15: ロギング・エラー処理整備

各Navigatorで少なくとも、

```text
開始
現在URL
対象RaceId / YearMonth
選択ルート
遷移後URL
解析ページ種類
```

をログへ残す。

個人情報等は扱わないため特別なマスキングは不要だが、HTML全文をログに残さない。

例外は最低限、

```text
JraNavigationException
JraPageParseException
```

へ整理する。

---

## Task 16: 実サイトE2Eテスト

最後にのみ実施する。

Parser unit testと混ぜない。

テストケース例。

```text
現在月Calendar取得
現在週RaceList取得
現在週RaceCard取得
完了済みRaceResult取得
最近のRaceResult取得
古いRaceResult取得
```

JRA側サイトの状態に依存するため、CIで常時実行するかは別途判断する。

例えば、

```csharp
[TestCategory("External")]
```

等を付ける。

---

# 36. 実装中に守ること

以下は禁止。

```csharp
JraCalendarPage.OpenRaceAsync()
JraRaceListPage.ClickRaceAsync()
JraRaceCardPage.GoToResultAsync()
```

`IJraPage` は操作オブジェクトにしない。

また、

```csharp
public IPage Page { get; }
public ILocator Locator { get; }
```

などPlaywrightオブジェクトをJRA公開APIへ漏らさない。

既存 `IWebBrowser` もJRAページモデルから参照しない。

---

# 37. 初期実装で意図的に割愛するもの

以下はこのフェーズでは完全実装不要。

```text
全JRAページ種類
オッズ
払戻金
調教情報
馬詳細ページ
騎手詳細
調教師詳細
血統
競走馬過去成績
レース映像
全RaceCard項目
全RaceResult項目
JRA内部race key
Strategy Patternによる全Navigation分割
```

必要になった時点で追加する。

まず、

```text
Calendar
↓
RaceList
↓
RaceCard
↓
RaceResult
```

の縦一本を完成させる。

---

# 38. 最終的な目標コード

完成後、アプリケーション側から以下程度で使用できる状態を目標とする。

```csharp
var calendarPage =
    await jra.Navigate.ToCalendarAsync(
        new YearMonth(2026, 9));

if (calendarPage is not JraCalendarPage calendar)
{
    throw new InvalidOperationException(
        $"Unexpected page: {calendarPage.Kind}");
}

var date =
    calendar.RaceDates
        .Select(x => x.Date)
        .First();

var raceListPage =
    await jra.Navigate.ToRaceListAsync(
        date,
        RaceCourse.Chukyo);

if (raceListPage is not JraRaceListPage raceList)
{
    throw new InvalidOperationException(
        $"Unexpected page: {raceListPage.Kind}");
}

var race =
    raceList.Races[0];

var raceCardPage =
    await jra.Navigate.ToRaceCardAsync(
        race.Id);

if (raceCardPage is JraRaceCardPage raceCard)
{
    Console.WriteLine(
        $"{raceCard.RaceName}: {raceCard.Entries.Count}頭");
}

var resultPage =
    await jra.Navigate.ToRaceResultAsync(
        race.Id);

if (resultPage is JraRaceResultPage result)
{
    foreach (var entry in result.Results)
    {
        Console.WriteLine(
            $"{entry.FinishPosition}: {entry.HorseName}");
    }
}
```

---

# 39. 実装優先順位

最初のまとまりとして以下まで完成させる。

```text
Task 1
↓
Task 2
↓
Task 3
↓
Task 4
↓
Task 5
↓
Task 6
↓
Task 7
```

ここで、

```csharp
Navigate.ToCalendarAsync(...)
Navigate.ToRaceListAsync(...)
```

まで実際に動作させる。

次に、

```text
Task 8
Task 9
```

で出馬表。

最後に、

```text
Task 10
Task 11
Task 12
Task 13
Task 14
```

で結果取得を完成させる。

各段階でビルド・テスト可能な状態を維持すること。

大きな一括実装は行わない。

---

# 40. 設計判断の要点

この設計では、一般的なPlaywright Page Objectとは少し異なり、

```text
JraCalendarPage
JraRaceListPage
JraRaceCardPage
JraRaceResultPage
```

をブラウザー操作オブジェクトにしない。

これらは、

```text
JRAの現在ページを構造化したImmutableなデータ
```

として扱う。

ブラウザー操作はすべて、

```csharp
JraSession.Navigate
```

へ集約する。

これにより、

```text
Navigationのテスト
Parserのテスト
Browserのテスト
```

を完全に分離できる。

またJRA側のサイト構成変更についても、

```text
リンク導線変更
    → Navigation修正

HTML・表示項目変更
    → Parser修正

Playwright操作変更
    → Browser修正
```

と影響範囲を限定できる。

この境界を実装中に崩さないこと。