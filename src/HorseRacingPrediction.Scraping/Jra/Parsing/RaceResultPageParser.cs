using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>
/// 特定レースの結果ページを解析する。着順・馬番・馬名列を持つテーブルを対象とする。
/// 実ページの具体的なURL構造は未調査のため、テーブルの見出し（ヘッダー）から
/// 列を特定する方式にして、ページ構造変更への耐性を優先している。
/// </summary>
public sealed class RaceResultPageParser
    : IJraPageParser
{
    private static readonly Regex DateRegex =
        new(@"(?<year>\d{4})年\s*(?<month>\d{1,2})月\s*(?<day>\d{1,2})日", RegexOptions.Compiled);

    // Task16実サイト確認で判明: 実ページの見出しは「1レース」のように「R」ではなく
    // 「レース」表記であり、旧正規表現（digit+"R"）は常にマッチしなかった。
    private static readonly Regex RaceNumberRegex =
        new(@"(?<num>\d{1,2})\s*(?:R|レース)", RegexOptions.Compiled);

    private static readonly Regex LeadingNumberRegex =
        new(@"(?<num>\d{1,2})", RegexOptions.Compiled);

    private static readonly Regex TimeSpanRegex =
        new(@"(?:(?<min>\d{1,2}):)?(?<sec>\d{1,2})\.(?<frac>\d{1})", RegexOptions.Compiled);

    // 天候/馬場は実ページのHTML構造が未調査のため、見出し・本文中のテキストパターンから
    // 緩やかに抽出する方式にしている。ラベル自体が存在しない場合は「天候・馬場欄なし」の
    // 正常系（null）として扱うが、ラベルは存在するのに続く値がJRAの既知値集合に無い場合は
    // 「未知の値」（依頼書10節・11節）としてJraUnexpectedValueExceptionにする。
    private static readonly Regex WeatherLabelRegex =
        new(@"天候\s*[:：]?\s*(?<value>\S+)", RegexOptions.Compiled);

    private static readonly string[] KnownWeatherValues = ["晴", "曇", "小雨", "雨", "小雪", "雪"];

    // 実サイト確認（2026-09-07）で判明: 実ページの馬場状態表記は「天候 雨 芝 稍重 ダート 重」
    // のように「馬場」「馬場状態」という語を伴わず、「芝」「ダート」の直後に状態値が
    // 続くのみ。旧正規表現は「馬場」の語を必須としていたため常にマッチしなかった。
    private static readonly Regex TurfConditionLabelRegex =
        new(@"芝\s*(?<value>\S+)", RegexOptions.Compiled);

    private static readonly Regex DirtConditionLabelRegex =
        new(@"ダート\s*(?<value>\S+)", RegexOptions.Compiled);

    private static readonly string[] KnownTrackConditionValues = ["良", "稍重", "重", "不良"];

    public JraPageKind Kind =>
        JraPageKind.RaceResult;

    public int Priority => 85;

    public bool CanParse(
        PageSnapshot snapshot)
    {
        return FindResultTable(snapshot) is not null;
    }

    public IJraPage Parse(
        PageSnapshot snapshot)
    {
        var table =
            FindResultTable(snapshot)
            ?? throw new JraPageParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "レース結果テーブルを取得できませんでした。");

        var date =
            ParseDate(snapshot);

        var course =
            ParseCourse(snapshot);

        var number =
            ParseRaceNumber(snapshot);

        var raceName =
            ParseRaceName(snapshot);

        var results =
            ParseResults(table, snapshot.Url);

        var weatherText =
            ParseWeatherText(snapshot);

        var trackConditionText =
            ParseTrackConditionText(snapshot);

        var payouts =
            ParsePayouts(snapshot, table);

        return new JraRaceResultPage(
            snapshot.Url,
            new RaceId(date, course, number),
            raceName,
            results,
            weatherText,
            trackConditionText,
            payouts is not null && !payouts.IsEmpty ? payouts : null);
    }

    private static string? ParseWeatherText(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var match =
            WeatherLabelRegex.Match(searchText);

        if (!match.Success)
        {
            // 「天候」ラベル自体が存在しない＝仕様上optionalな要素が存在しない正常系。
            return null;
        }

        var value = match.Groups["value"].Value;

        if (!KnownWeatherValues.Contains(value, StringComparer.Ordinal))
        {
            throw new JraUnexpectedValueException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "Weather",
                value);
        }

        return value;
    }

    private static string? ParseTrackConditionText(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var turfMatch = TurfConditionLabelRegex.Match(searchText);
        var dirtMatch = DirtConditionLabelRegex.Match(searchText);

        if (!turfMatch.Success && !dirtMatch.Success)
        {
            return null;
        }

        var parts = new List<string>();
        if (turfMatch.Success)
        {
            var value = turfMatch.Groups["value"].Value;

            if (!KnownTrackConditionValues.Contains(value, StringComparer.Ordinal))
            {
                throw new JraUnexpectedValueException(
                    JraPageKind.RaceResult,
                    snapshot.Url,
                    "TrackCondition(Turf)",
                    value);
            }

            parts.Add($"芝:{value}");
        }

        if (dirtMatch.Success)
        {
            var value = dirtMatch.Groups["value"].Value;

            if (!KnownTrackConditionValues.Contains(value, StringComparer.Ordinal))
            {
                throw new JraUnexpectedValueException(
                    JraPageKind.RaceResult,
                    snapshot.Url,
                    "TrackCondition(Dirt)",
                    value);
            }

            parts.Add($"ダート:{value}");
        }

        return string.Join(" ", parts);
    }

    private static RacePayouts? ParsePayouts(
        PageSnapshot snapshot,
        PageTableSnapshot resultTable)
    {
        var winPayouts = new List<PayoutLine>();
        var placePayouts = new List<PayoutLine>();
        var quinellaPayouts = new List<PayoutLine>();
        var exactaPayouts = new List<PayoutLine>();
        var trifectaPayouts = new List<PayoutLine>();

        foreach (var table in snapshot.Tables)
        {
            if (ReferenceEquals(table, resultTable))
            {
                continue;
            }

            var typeColumnIndex = FindPayoutTypeColumnIndex(table.Headers);
            var combinationColumnIndex = FindPayoutCombinationColumnIndex(table.Headers);
            var amountColumnIndex = FindPayoutAmountColumnIndex(table.Headers);

            if (combinationColumnIndex < 0 || amountColumnIndex < 0)
            {
                continue;
            }

            string? currentTypeName = null;

            foreach (var row in table.Rows)
            {
                if (typeColumnIndex >= 0 && typeColumnIndex < row.Count &&
                    !string.IsNullOrWhiteSpace(row[typeColumnIndex]))
                {
                    currentTypeName = row[typeColumnIndex].Trim();
                }

                if (currentTypeName is null ||
                    combinationColumnIndex >= row.Count ||
                    amountColumnIndex >= row.Count)
                {
                    continue;
                }

                var bucket = currentTypeName switch
                {
                    "単勝" => winPayouts,
                    "複勝" => placePayouts,
                    "馬連" => quinellaPayouts,
                    "馬単" => exactaPayouts,
                    "三連単" => trifectaPayouts,
                    _ => null,
                };

                if (bucket is null)
                {
                    continue;
                }

                AppendPayoutLines(bucket, row[combinationColumnIndex], row[amountColumnIndex]);
            }
        }

        return new RacePayouts(winPayouts, placePayouts, quinellaPayouts, exactaPayouts, trifectaPayouts);
    }

    private static void AppendPayoutLines(
        List<PayoutLine> bucket,
        string combinationCell,
        string amountCell)
    {
        var combinations = SplitPayoutCellLines(combinationCell);
        var amounts = SplitPayoutCellLines(amountCell);

        for (var i = 0; i < combinations.Count; i++)
        {
            var amountText = i < amounts.Count ? amounts[i] : amounts.LastOrDefault();

            if (string.IsNullOrWhiteSpace(amountText))
            {
                continue;
            }

            var digitsOnly = new string(amountText.Where(char.IsDigit).ToArray());

            if (digitsOnly.Length == 0)
            {
                continue;
            }

            bucket.Add(new PayoutLine(combinations[i], decimal.Parse(digitsOnly)));
        }
    }

    private static List<string> SplitPayoutCellLines(string cell)
        => cell
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToList();

    private static int FindPayoutTypeColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("式別", StringComparison.Ordinal) ||
                headers[i].Contains("券種", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindPayoutCombinationColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("組合せ", StringComparison.Ordinal) ||
                RemoveWhitespace(headers[i]).Contains("馬番", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindPayoutAmountColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("払戻", StringComparison.Ordinal) ||
                headers[i].Contains("金額", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static PageTableSnapshot? FindResultTable(
        PageSnapshot snapshot)
    {
        foreach (var table in snapshot.Tables)
        {
            if (FindFinishPositionColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            if (FindHorseNumberColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            if (FindHorseNameColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            return table;
        }

        return null;
    }

    private static int FindFinishPositionColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("着順", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindHorseNumberColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            // Task16実サイト確認で判明: 実ページのヘッダーはセル内改行により
            // 「馬 番」のように空白入りで取得される。空白を除去して比較する。
            if (RemoveWhitespace(headers[i]).Contains("馬番", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string RemoveWhitespace(string value)
        => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    private static int FindHorseNameColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("馬名", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindJockeyColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("騎手", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindTimeColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("タイム", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // 性齢列は「性齢」列見出しがあるページでのみ解析する（依頼書33節: 実サイトの
    // 列構成が本タスクの既存Fixtureでは未確認のため、列が存在しない場合は正常に
    // null/nullを返し、列が存在するのに値を解析できない場合のみエラーとする）。
    private static int FindSexAgeColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("性齢", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static readonly Regex SexAgeRegex =
        new(@"^(?<sex>牡|牝|せん)(?<age>\d{1,2})$", RegexOptions.Compiled);

    private static DateOnly ParseDate(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var match =
            DateRegex.Match(searchText);

        if (!match.Success)
        {
            throw new JraPageParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "対象日付を取得できませんでした。");
        }

        return new DateOnly(
            int.Parse(match.Groups["year"].Value),
            int.Parse(match.Groups["month"].Value),
            int.Parse(match.Groups["day"].Value));
    }

    private static RaceCourse ParseCourse(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var course =
            RaceCourseNames.Parse(searchText);

        if (course == RaceCourse.Unknown)
        {
            throw new JraPageParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "対象競馬場を取得できませんでした。");
        }

        return course;
    }

    private static int ParseRaceNumber(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var match =
            RaceNumberRegex.Match(searchText);

        if (!match.Success)
        {
            throw new JraPageParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "対象レース番号を取得できませんでした。");
        }

        return int.Parse(match.Groups["num"].Value);
    }

    // レース結果ページのタイトル見出し（サイト全体のマストヘッド「JRA 日本中央競馬会」等）を
    // 誤ってレース名として拾わないための除外リスト。
    private static readonly string[] NonRaceNameHeadings =
    [
        "JRA 日本中央競馬会",
        "払戻金",
        "勝馬の紹介",
        "JRAからのお知らせ",
        "Footer",
    ];

    /// <summary>
    /// 実サイト確認（2026-09-07）で判明: 見出し一覧の先頭は常にサイト全体の
    /// マストヘッド見出し「JRA 日本中央競馬会」であり、旧実装（「競馬場名でも日付でも
    /// ない最初の見出し」という緩い判定）はこれを誤ってレース名として採用していた
    /// （実運用で全レースのRaceNameが"JRA 日本中央競馬会"になる事象として確認）。
    /// 実際のレース名（例：「障害3歳以上未勝利」）は、日付・競馬場・レース番号が
    /// すべて含まれる見出し（例：「レース結果 2026年9月6日（日曜）4回中山2日 1レース」）の
    /// 直後に続くことを確認したため、その見出しを起点にレース名を探す。
    /// 成績ページでは毎回レース名が確定しているはずの情報のため、特定できない場合は
    /// 他の識別情報（日付・競馬場・レース番号）と同様に例外として扱う。
    /// </summary>
    private static string ParseRaceName(
        PageSnapshot snapshot)
    {
        var headings = snapshot.Headings;
        var metaIndex = -1;
        for (var i = 0; i < headings.Count; i++)
        {
            if (DateRegex.IsMatch(headings[i]) && RaceNumberRegex.IsMatch(headings[i]))
            {
                metaIndex = i;
                break;
            }
        }

        if (metaIndex >= 0)
        {
            for (var i = metaIndex + 1; i < headings.Count; i++)
            {
                var candidate = headings[i].Trim();

                if (string.IsNullOrWhiteSpace(candidate) ||
                    RaceCourseNames.Parse(candidate) != RaceCourse.Unknown ||
                    DateRegex.IsMatch(candidate) ||
                    NonRaceNameHeadings.Contains(candidate, StringComparer.Ordinal))
                {
                    continue;
                }

                return candidate;
            }
        }

        throw new JraPageParseException(
            JraPageKind.RaceResult,
            snapshot.Url,
            "レース名を取得できませんでした。ページ構造が想定と異なる可能性があります。");
    }

    private static IReadOnlyList<RaceResultEntry> ParseResults(
        PageTableSnapshot table,
        string url)
    {
        var finishIndex = FindFinishPositionColumnIndex(table.Headers);
        var horseNumberIndex = FindHorseNumberColumnIndex(table.Headers);
        var horseNameIndex = FindHorseNameColumnIndex(table.Headers);
        var jockeyIndex = FindJockeyColumnIndex(table.Headers);
        var timeIndex = FindTimeColumnIndex(table.Headers);
        var sexAgeIndex = FindSexAgeColumnIndex(table.Headers);

        var results = new List<RaceResultEntry>();

        foreach (var row in table.Rows)
        {
            // Task16実サイト確認で判明: 抽出したテーブルの1行目にヘッダー行自体が
            // 重複して含まれることがある。見出し文字列をレース結果として扱わない
            // よう読み飛ばす。
            if (row.Count > 0 && table.Headers.Count > 0 &&
                string.Equals(row[0], table.Headers[0], StringComparison.Ordinal))
            {
                continue;
            }

            if (finishIndex >= row.Count ||
                horseNumberIndex >= row.Count ||
                horseNameIndex >= row.Count)
            {
                continue;
            }

            var finishText = row[finishIndex].Trim();

            // 数字着順以外に、JRAの正常な特殊状態（取消・除外・中止・失格）を
            // ResultStatusとしてモデル化する（依頼書16節）。従来はここで単純に
            // 行を読み飛ばしていたため、これらの馬が結果からサイレントに欠落していた。
            ResultStatus status;
            int? finishPosition;

            var finishMatch = LeadingNumberRegex.Match(finishText);

            if (finishMatch.Success)
            {
                status = ResultStatus.Finished;
                finishPosition = int.Parse(finishMatch.Groups["num"].Value);
            }
            else if (ResultStatusText.TryParse(finishText, out var specialStatus))
            {
                status = specialStatus;
                finishPosition = null;
            }
            else if (string.IsNullOrWhiteSpace(finishText))
            {
                // 着順欄が完全に空白の行は、罫線用の空行等パース対象外の行である
                // 可能性が高く、既存の挙動（読み飛ばし）を維持する。
                continue;
            }
            else
            {
                throw new JraUnexpectedValueException(
                    JraPageKind.RaceResult,
                    url,
                    "ResultStatus",
                    finishText);
            }

            var numberMatch =
                LeadingNumberRegex.Match(row[horseNumberIndex]);

            if (!numberMatch.Success)
            {
                continue;
            }

            var horseNumber =
                int.Parse(numberMatch.Groups["num"].Value);

            var horseName = row[horseNameIndex];

            if (string.IsNullOrWhiteSpace(horseName))
            {
                continue;
            }

            var jockeyName =
                jockeyIndex >= 0 && jockeyIndex < row.Count && !string.IsNullOrWhiteSpace(row[jockeyIndex])
                    ? row[jockeyIndex]
                    : null;

            TimeSpan? time = null;

            if (timeIndex >= 0 && timeIndex < row.Count)
            {
                var timeMatch = TimeSpanRegex.Match(row[timeIndex]);

                if (timeMatch.Success)
                {
                    var minutes =
                        timeMatch.Groups["min"].Success
                            ? int.Parse(timeMatch.Groups["min"].Value)
                            : 0;

                    var seconds = int.Parse(timeMatch.Groups["sec"].Value);
                    var fractionTenths = int.Parse(timeMatch.Groups["frac"].Value);

                    time = new TimeSpan(0, 0, minutes, seconds, fractionTenths * 100);
                }
            }

            // Finishedであれば必須（依頼書19節）。値が存在するのに解析できない場合と
            // 区別できるよう、タイム列自体が存在するかどうかで判定する。
            if (status == ResultStatus.Finished &&
                time is null &&
                timeIndex >= 0 && timeIndex < row.Count &&
                !string.IsNullOrWhiteSpace(row[timeIndex]))
            {
                throw new JraValueParseException(
                    JraPageKind.RaceResult,
                    url,
                    "Time",
                    row[timeIndex]);
            }

            HorseSex? sex = null;
            int? age = null;

            if (sexAgeIndex >= 0 && sexAgeIndex < row.Count && !string.IsNullOrWhiteSpace(row[sexAgeIndex]))
            {
                var sexAgeText = row[sexAgeIndex].Trim();
                var sexAgeMatch = SexAgeRegex.Match(sexAgeText);

                if (!sexAgeMatch.Success)
                {
                    throw new JraUnexpectedValueException(
                        JraPageKind.RaceResult,
                        url,
                        "Sex",
                        sexAgeText);
                }

                if (!HorseSexText.TryParse(sexAgeMatch.Groups["sex"].Value, out var parsedSex))
                {
                    throw new JraUnexpectedValueException(
                        JraPageKind.RaceResult,
                        url,
                        "Sex",
                        sexAgeText);
                }

                sex = parsedSex;
                age = int.Parse(sexAgeMatch.Groups["age"].Value);
            }

            results.Add(new RaceResultEntry(
                status,
                finishPosition,
                horseNumber,
                horseName,
                jockeyName,
                time,
                Sex: sex,
                Age: age));
        }

        return results;
    }
}
