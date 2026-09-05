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
    // 緩やかに抽出する方式にしている（見つからない場合は null を返し、既存の着順取得は妨げない）。
    private static readonly Regex WeatherRegex =
        new(@"天候\s*[:：]?\s*(?<value>晴|曇|雨|小雨|雪|小雪)", RegexOptions.Compiled);

    private static readonly Regex TrackConditionRegex =
        new(@"(?<surface>芝|ダート)?\s*馬場(?:状態)?\s*[:：]?\s*(?<value>良|稍重|重|不良)", RegexOptions.Compiled);

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
            ParseResults(table);

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
            WeatherRegex.Match(searchText);

        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? ParseTrackConditionText(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var match =
            TrackConditionRegex.Match(searchText);

        return match.Success ? match.Groups["value"].Value : null;
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

    private static string? ParseRaceName(
        PageSnapshot snapshot)
    {
        foreach (var heading in snapshot.Headings)
        {
            var withoutNumber =
                RaceNumberRegex.Replace(heading, string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(withoutNumber) &&
                RaceCourseNames.Parse(withoutNumber) == RaceCourse.Unknown &&
                !DateRegex.IsMatch(withoutNumber))
            {
                return withoutNumber;
            }
        }

        return null;
    }

    private static IReadOnlyList<RaceResultEntry> ParseResults(
        PageTableSnapshot table)
    {
        var finishIndex = FindFinishPositionColumnIndex(table.Headers);
        var horseNumberIndex = FindHorseNumberColumnIndex(table.Headers);
        var horseNameIndex = FindHorseNameColumnIndex(table.Headers);
        var jockeyIndex = FindJockeyColumnIndex(table.Headers);
        var timeIndex = FindTimeColumnIndex(table.Headers);

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

            var finishMatch =
                LeadingNumberRegex.Match(row[finishIndex]);

            if (!finishMatch.Success)
            {
                continue;
            }

            var finishPosition =
                int.Parse(finishMatch.Groups["num"].Value);

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

            results.Add(new RaceResultEntry(
                finishPosition,
                horseNumber,
                horseName,
                jockeyName,
                time));
        }

        return results;
    }
}
