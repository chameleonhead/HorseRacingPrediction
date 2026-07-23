using System.Globalization;
using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed class JraScheduleCalendarParser : IJraStructuredPageParser<JraScheduleCalendarPage>
{
    public JraStructuredPageParseResult<JraScheduleCalendarPage> Parse(PageSnapshot snapshot)
    {
        var issues = new List<JraPageParseIssue>();
        var (year, month) = JraPageParserText.ExtractYearMonth(snapshot);

        if (year is null)
        {
            issues.Add(new JraPageParseIssue(
                "schedule.calendar.year_missing",
                JraPageDiagnosticSeverity.Warning,
                "開催日程ページから年を確定できませんでした。"));
        }

        if (month is null)
        {
            issues.Add(new JraPageParseIssue(
                "schedule.calendar.month_missing",
                JraPageDiagnosticSeverity.Error,
                "開催日程ページから月を確定できませんでした。"));
        }

        var monthLinks = snapshot.Links
            .Select(link => new { Link = link, Month = JraPageParserText.ExtractMonth(link.Title) })
            .Where(x => x.Month is not null)
            .Select(x => new JraCalendarMonthLink(
                x.Month!.Value,
                x.Link.Title,
                JraPageParserText.ResolveUrl(snapshot.Url, x.Link.Url),
                month is not null && x.Month.Value == month.Value))
            .DistinctBy(x => x.Month)
            .OrderBy(x => x.Month)
            .ToList();

        var cells = snapshot.Tables
            .SelectMany(table => table.Rows)
            .SelectMany(row => row)
            .Concat(snapshot.MainText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var scheduledDays = new Dictionary<DateOnly, JraRaceScheduleDay>();
        foreach (var cell in cells)
        {
            if (year is null || month is null)
            {
                continue;
            }

            var day = JraPageParserText.TryParseScheduleDayCell(cell, year.Value, month.Value);
            if (day is not null)
            {
                scheduledDays[day.Date] = day;
            }
        }

        if (scheduledDays.Count == 0)
        {
            issues.Add(new JraPageParseIssue(
                "schedule.calendar.days_missing",
                JraPageDiagnosticSeverity.Warning,
                "開催日程カレンダーから開催日セルを検出できませんでした。"));
        }

        var nextLinks = monthLinks
            .Select(link => new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenMonth,
                link.Text,
                link.Url,
                JraPageParserText.InferNavigationMode(link.Url)))
            .ToList();

        var data = new JraScheduleCalendarPage(
            snapshot.Url,
            year,
            month,
            monthLinks,
            scheduledDays.Values.OrderBy(day => day.Date).ToList(),
            issues);

        return new JraStructuredPageParseResult<JraScheduleCalendarPage>(
            year is not null && month is not null,
            data,
            issues,
            scheduledDays.Count > 0 ? JraPageParseConfidence.High : JraPageParseConfidence.Medium,
            nextLinks,
            year is not null && month is not null ? null : "開催日程カレンダーページの構造解析に失敗しました。");
    }
}