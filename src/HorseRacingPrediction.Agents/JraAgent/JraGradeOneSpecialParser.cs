using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

public sealed class JraGradeOneSpecialParser : IJraStructuredPageParser<JraGradeOneSpecialPage>
{
    private static readonly HashSet<string> TabLabels =
    [
        "レーストップ",
        "出馬表",
        "出走馬情報",
        "調教動画ほか",
        "データ分析",
        "プレレーティング",
        "プレイバック",
    ];

    private static readonly HashSet<string> MisleadingHeadings =
    [
        "検索ウィンドウ",
        "ニュース",
        "JRA",
    ];

    public JraStructuredPageParseResult<JraGradeOneSpecialPage> Parse(PageSnapshot snapshot)
    {
        var issues = new List<JraPageParseIssue>();
        var slug = JraPageParserText.ExtractG1Slug(snapshot.Url);
        var raceName = JraPageParserText.ExtractRaceNameFromTitle(snapshot.Title)
            ?? snapshot.Headings.FirstOrDefault(h => !MisleadingHeadings.Contains(h));
        var grade = snapshot.MainText.Contains("GⅠ", StringComparison.Ordinal) ? "GⅠ" : null;
        var raceDate = JraPageParserText.ExtractFirstFullDate(snapshot.Headings.Concat([snapshot.MainText]));
        var racecourse = JraPageParserText.ExtractRacecourses(snapshot.MainText).FirstOrDefault();
        var distance = JraPageParserText.ExtractDistance(snapshot.MainText);

        var tabs = snapshot.Links
            .Where(link => TabLabels.Any(label => JraPageParserText.ContainsNormalized(link.Title, label)))
            .Where(link => JraPageParserText.IsRelevantG1TabUrl(slug, link.Url))
            .Select(link => new JraSpecialPageTab(link.Title, JraPageParserText.ResolveUrl(snapshot.Url, link.Url)))
            .DistinctBy(tab => JraPageParserText.Normalize(tab.Label))
            .ToList();

        if (tabs.Count == 0)
        {
            issues.Add(new JraPageParseIssue(
                "g1.tabs_missing",
                JraPageDiagnosticSeverity.Warning,
                "G1 特設ページから主要タブ導線を検出できませんでした。"));
        }

        if (string.IsNullOrWhiteSpace(raceName))
        {
            issues.Add(new JraPageParseIssue(
                "g1.race_name_missing",
                JraPageDiagnosticSeverity.Error,
                "G1 特設ページからレース名を検出できませんでした。"));
        }

        var relatedNews = snapshot.Links
            .Where(link => !string.IsNullOrWhiteSpace(raceName)
                && (JraPageParserText.ContainsNormalized(link.Title, raceName)
                    || JraPageParserText.ContainsNormalized(link.Title, "枠順確定")
                    || JraPageParserText.ContainsNormalized(link.Title, "プレレーティング")))
            .Select(link => new JraSpecialPageNewsItem(link.Title, JraPageParserText.ResolveUrl(snapshot.Url, link.Url)))
            .DistinctBy(item => item.Title)
            .Take(10)
            .ToList();

        var nextLinks = tabs
            .Select(tab => new JraStructuredPageNextLink(
                JraPageParserText.ContainsNormalized(tab.Label, "出馬表") ? JraStructuredLinkRelations.OpenRaceCard :
                JraPageParserText.ContainsNormalized(tab.Label, "出走馬情報") ? JraStructuredLinkRelations.OpenHorseInfo :
                JraPageParserText.ContainsNormalized(tab.Label, "データ分析") ? JraStructuredLinkRelations.OpenData :
                JraStructuredLinkRelations.OpenRelated,
                tab.Label,
                tab.Url,
                JraPageParserText.InferNavigationMode(tab.Url)))
            .Concat(relatedNews.Select(item => new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenRelated,
                item.Title,
                item.Url,
                JraPageParserText.InferNavigationMode(item.Url))))
            .ToList();

        var confidence = !string.IsNullOrWhiteSpace(raceName) && tabs.Count > 0
            ? JraPageParseConfidence.High
            : !string.IsNullOrWhiteSpace(raceName)
                ? JraPageParseConfidence.Medium
                : JraPageParseConfidence.Low;

        var data = new JraGradeOneSpecialPage(snapshot.Url, raceName, grade, raceDate, racecourse, distance, tabs, relatedNews, issues);
        return new JraStructuredPageParseResult<JraGradeOneSpecialPage>(
            !string.IsNullOrWhiteSpace(raceName),
            data,
            issues,
            confidence,
            nextLinks,
            !string.IsNullOrWhiteSpace(raceName) ? null : "G1 特設ページの解析に失敗しました。");
    }
}