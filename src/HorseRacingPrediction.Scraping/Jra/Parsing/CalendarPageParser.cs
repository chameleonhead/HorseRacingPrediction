using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>
/// JRA開催日程ページ（/keiba/calendar/）を解析する。
/// カレンダーは1週間=1行のテーブルとして描画され、各セルのテキストは
/// 「日番号 [地方] [競馬場名 [レース名(グレード)]]...」の形式になる
/// （<see cref="Browser.PlaywrightWebBrowser"/> がセル内の改行を空白へ正規化するため）。
/// </summary>
public sealed class CalendarPageParser
    : IJraPageParser
{
    private static readonly Regex YearMonthRegex =
        new(@"(?<year>\d{4})年\s*(?<month>\d{1,2})月", RegexOptions.Compiled);

    private static readonly Regex LeadingDayRegex =
        new(@"^\s*(?<day>\d{1,2})\b", RegexOptions.Compiled);

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

    private static YearMonth ParseMonth(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var match =
            YearMonthRegex.Match(searchText);

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

    private static IReadOnlyList<JraRaceDate> ParseRaceDates(
        PageSnapshot snapshot,
        YearMonth month)
    {
        var results =
            new Dictionary<DateOnly, List<RaceCourse>>();

        var daysInMonth =
            DateTime.DaysInMonth(month.Year, month.Month);

        foreach (var cellText in snapshot.Tables.SelectMany(table =>
                     table.Rows.SelectMany(row => row)))
        {
            AddFromCellText(cellText, month, daysInMonth, results);
        }

        if (results.Count == 0)
        {
            // テーブルとして抽出できなかった場合のフォールバック。
            foreach (var section in snapshot.Sections)
            {
                AddFromCellText(section.MainText, month, daysInMonth, results);
            }
        }

        return results
            .OrderBy(x => x.Key)
            .Select(x => new JraRaceDate(x.Key, x.Value))
            .ToArray();
    }

    private static void AddFromCellText(
        string cellText,
        YearMonth month,
        int daysInMonth,
        Dictionary<DateOnly, List<RaceCourse>> results)
    {
        if (string.IsNullOrWhiteSpace(cellText))
        {
            return;
        }

        var dayMatch =
            LeadingDayRegex.Match(cellText);

        if (!dayMatch.Success)
        {
            return;
        }

        var day =
            int.Parse(dayMatch.Groups["day"].Value);

        if (day < 1 || day > daysInMonth)
        {
            return;
        }

        var courses =
            RaceCourseNames.ParseAll(cellText);

        if (courses.Count == 0)
        {
            return;
        }

        var date =
            new DateOnly(month.Year, month.Month, day);

        if (!results.TryGetValue(date, out var existing))
        {
            existing = [];
            results[date] = existing;
        }

        foreach (var course in courses)
        {
            if (!existing.Contains(course))
            {
                existing.Add(course);
            }
        }
    }
}
