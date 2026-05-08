using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

public sealed class JraKeibaMenuParser : IJraStructuredPageParser<JraKeibaMenuPage>
{
    private static readonly string[] PrimaryMenuNames =
    [
        "開催日程",
        "出馬表",
        "オッズ",
        "レース結果",
        "払戻金",
        "今週の注目レース",
        "馬場情報",
    ];

    public JraStructuredPageParseResult<JraKeibaMenuPage> Parse(PageSnapshot snapshot)
    {
        var issues = new List<JraPageParseIssue>();
        var entries = snapshot.Links
            .Concat(snapshot.Actions.Select(a => new SearchResultLink(snapshot.Url, a.Text)))
            .Where(link => PrimaryMenuNames.Any(name => JraPageParserText.ContainsNormalized(link.Title, name)))
            .Select(link => new JraNavigationMenuEntry(
                link.Title,
                JraPageParserText.ResolveUrl(snapshot.Url, link.Url),
                true))
            .DistinctBy(entry => JraPageParserText.Normalize(entry.Text))
            .ToList();

        var scheduleEntry = entries.FirstOrDefault(entry => JraPageParserText.ContainsNormalized(entry.Text, "開催日程"));
        if (scheduleEntry is null)
        {
            issues.Add(new JraPageParseIssue(
                "keiba.menu.schedule_missing",
                JraPageDiagnosticSeverity.Error,
                "競馬メニューから『開催日程』の導線を検出できませんでした。"));
        }

        var nextLinks = entries
            .Select(entry => new JraStructuredPageNextLink(
                JraPageParserText.ContainsNormalized(entry.Text, "開催日程") ? JraStructuredLinkRelations.OpenSchedule : JraStructuredLinkRelations.MenuEntry,
                entry.Text,
                entry.Url,
                JraPageParserText.InferNavigationMode(entry.Url)))
            .ToList();

        var data = new JraKeibaMenuPage(snapshot.Url, entries, scheduleEntry?.Text, issues);
        return new JraStructuredPageParseResult<JraKeibaMenuPage>(
            scheduleEntry is not null,
            data,
            issues,
            scheduleEntry is not null ? JraPageParseConfidence.High : JraPageParseConfidence.Medium,
            nextLinks,
            scheduleEntry is null ? "競馬メニュー上の開催日程リンクが見つかりませんでした。" : null);
    }
}