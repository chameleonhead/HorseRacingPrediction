using System.Globalization;
using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed class JraRaceListParser : IJraStructuredPageParser<JraRaceListPage>
{
    public JraStructuredPageParseResult<JraRaceListPage> Parse(PageSnapshot snapshot)
    {
        var issues = new List<JraPageParseIssue>();
        var (year, month) = JraPageParserText.ExtractYearMonth(snapshot);
        var day = JraPageParserText.ExtractDay(snapshot.Headings.Concat([snapshot.MainText]));
        var racecourse = JraPageParserText.ExtractRacecourses(snapshot.MainText).FirstOrDefault()
            ?? snapshot.Headings.SelectMany(JraPageParserText.ExtractRacecourses).FirstOrDefault();

        DateOnly? raceDate = null;
        if (year is not null && month is not null && day is not null)
        {
            try
            {
                raceDate = new DateOnly(year.Value, month.Value, day.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                issues.Add(new JraPageParseIssue(
                    "race.list.invalid_date",
                    JraPageDiagnosticSeverity.Warning,
                    "レース一覧ページから抽出した日付が不正でした。"));
            }
        }

        var races = snapshot.Links
            .Select(link => new { Link = link, RaceNumber = JraPageParserText.ExtractRaceNumber(link.Title) })
            .Where(x => x.RaceNumber is not null)
            .Select(x => new JraRaceListEntry(x.RaceNumber!.Value, x.Link.Title, JraPageParserText.ResolveUrl(snapshot.Url, x.Link.Url)))
            .DistinctBy(x => x.RaceNumber)
            .OrderBy(x => x.RaceNumber)
            .ToList();

        if (races.Count == 0)
        {
            issues.Add(new JraPageParseIssue(
                "race.list.entries_missing",
                JraPageDiagnosticSeverity.Warning,
                "レース一覧ページからレース番号リンクを検出できませんでした。"));
        }

        var nextLinks = races
            .Select(race => new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenRace,
                race.Label,
                race.Url,
                string.IsNullOrWhiteSpace(race.Url)
                    ? JraStructuredLinkNavigationMode.CurrentSessionAction
                    : JraPageParserText.InferNavigationMode(race.Url)))
            .ToList();

        var data = new JraRaceListPage(snapshot.Url, raceDate, racecourse, races, issues);
        return new JraStructuredPageParseResult<JraRaceListPage>(
            races.Count > 0,
            data,
            issues,
            races.Count > 0 ? JraPageParseConfidence.High : JraPageParseConfidence.Low,
            nextLinks,
            races.Count > 0 ? null : "レース一覧ページの解析に失敗しました。");
    }
}