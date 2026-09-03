using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>
/// 特定開催日・競馬場のレース一覧ページを解析する。
/// レース番号列・レース名列・発走時刻列を持つテーブルを対象とする。
/// 実ページの具体的なURL構造は未調査のため、テーブルの見出し（ヘッダー）から
/// 列を特定する方式にして、ページ構造変更への耐性を優先している。
/// </summary>
internal sealed class RaceListPageParser
    : IJraPageParser
{
    private static readonly Regex DateRegex =
        new(@"(?<year>\d{4})年\s*(?<month>\d{1,2})月\s*(?<day>\d{1,2})日", RegexOptions.Compiled);

    private static readonly Regex LeadingNumberRegex =
        new(@"(?<num>\d{1,2})", RegexOptions.Compiled);

    private static readonly Regex TimeRegex =
        new(@"(?<hour>\d{1,2}):(?<minute>\d{2})", RegexOptions.Compiled);

    public JraPageKind Kind =>
        JraPageKind.RaceList;

    public int Priority => 90;

    public bool CanParse(
        PageSnapshot snapshot)
    {
        return FindRaceTable(snapshot) is not null;
    }

    public IJraPage Parse(
        PageSnapshot snapshot)
    {
        var table =
            FindRaceTable(snapshot)
            ?? throw new JraPageParseException(
                JraPageKind.RaceList,
                snapshot.Url,
                "レース一覧テーブルを取得できませんでした。");

        var date =
            ParseDate(snapshot);

        var course =
            ParseCourse(snapshot);

        var races =
            ParseRaces(table, date, course);

        return new JraRaceListPage(
            snapshot.Url,
            date,
            course,
            races);
    }

    private static PageTableSnapshot? FindRaceTable(
        PageSnapshot snapshot)
    {
        foreach (var table in snapshot.Tables)
        {
            if (FindNumberColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            if (FindNameColumnIndex(table.Headers) < 0 &&
                FindTimeColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            return table;
        }

        return null;
    }

    private static int FindNumberColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];

            // 実サイトのヘッダーは「レース」「番号」がセル内改行で
            // 分割され、空白区切りの「レース 番号」として取得される
            // （PlaywrightWebBrowser がセル内改行を空白へ正規化するため）。
            // 空白を除去してから比較する。
            var normalized =
                RemoveWhitespace(header);

            if (header is "R" ||
                normalized.Contains("レース番号", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string RemoveWhitespace(string value)
        => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    private static int FindNameColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("レース名", StringComparison.Ordinal))
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
            if (headers[i].Contains("発走", StringComparison.Ordinal))
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
                JraPageKind.RaceList,
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
                JraPageKind.RaceList,
                snapshot.Url,
                "対象競馬場を取得できませんでした。");
        }

        return course;
    }

    private static IReadOnlyList<RaceSummary> ParseRaces(
        PageTableSnapshot table,
        DateOnly date,
        RaceCourse course)
    {
        var numberIndex = FindNumberColumnIndex(table.Headers);
        var nameIndex = FindNameColumnIndex(table.Headers);
        var timeIndex = FindTimeColumnIndex(table.Headers);

        var races = new List<RaceSummary>();

        foreach (var row in table.Rows)
        {
            if (numberIndex >= row.Count)
            {
                continue;
            }

            var numberMatch =
                LeadingNumberRegex.Match(row[numberIndex]);

            if (!numberMatch.Success)
            {
                continue;
            }

            var number =
                int.Parse(numberMatch.Groups["num"].Value);

            if (number is < 1 or > 12)
            {
                continue;
            }

            var name =
                nameIndex >= 0 && nameIndex < row.Count && !string.IsNullOrWhiteSpace(row[nameIndex])
                    ? row[nameIndex]
                    : null;

            TimeOnly? startTime = null;

            if (timeIndex >= 0 && timeIndex < row.Count)
            {
                var timeMatch =
                    TimeRegex.Match(row[timeIndex]);

                if (timeMatch.Success)
                {
                    startTime = new TimeOnly(
                        int.Parse(timeMatch.Groups["hour"].Value),
                        int.Parse(timeMatch.Groups["minute"].Value));
                }
            }

            races.Add(new RaceSummary(
                new RaceId(date, course, number),
                name,
                startTime,
                RaceCardUrl: null,
                ResultUrl: null));
        }

        return races;
    }
}
