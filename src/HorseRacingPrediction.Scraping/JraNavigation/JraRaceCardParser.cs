using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed class JraRaceCardParser : IJraStructuredPageParser<JraRaceCardPage>
{
    private static readonly string[] TabLabels =
    [
        "レーストップ",
        "出馬表",
        "オッズ",
        "払戻金",
        "レース結果",
        "出走馬情報",
        "調教動画ほか",
        "データ分析",
        "プレレーティング",
        "過去の成績",
    ];

    private static readonly string[] IgnoredHeadings =
    [
        "出馬表",
        "JRA",
        "GIレース",
    ];

    public JraStructuredPageParseResult<JraRaceCardPage> Parse(PageSnapshot snapshot)
    {
        var issues = new List<JraPageParseIssue>();
        var raceName = JraPageParserText.ExtractRaceNameFromTitle(snapshot.Title)
            ?? snapshot.Headings.FirstOrDefault(IsRaceHeading);
        var raceDate = JraPageParserText.ExtractFirstFullDate(snapshot.Headings.Concat([snapshot.MainText]));
        var racecourse = JraPageParserText.ExtractRacecourses(snapshot.MainText).FirstOrDefault();
        var distance = JraPageParserText.ExtractDistance(snapshot.MainText);

        var explicitTabs = snapshot.Links
            .Select(link => new JraStructuredPageNextLink(
                MapRelation(link.Title),
                link.Title,
                JraPageParserText.ResolveUrl(snapshot.Url, link.Url),
                JraPageParserText.InferNavigationMode(link.Url)))
            .Concat(snapshot.Actions
                .Select(action => new JraStructuredPageNextLink(
                    MapRelation(action.Text),
                    action.Text,
                    null,
                    JraStructuredLinkNavigationMode.CurrentSessionAction)))
            .Where(link => TabLabels.Any(label => JraPageParserText.ContainsNormalized(link.Label, label)))
            .DistinctBy(link => JraPageParserText.Normalize(link.Label))
            .ToList();

        var nextLinks = explicitTabs
            .Concat(BuildImplicitRacePageLinks(snapshot.Url))
            .DistinctBy(link => link.Relation + "|" + JraPageParserText.Normalize(link.Label))
            .ToList();

        if (nextLinks.Count == 0)
        {
            issues.Add(new JraPageParseIssue(
                "race_card.tabs_missing",
                JraPageDiagnosticSeverity.Warning,
                "出馬表ページから主要タブ導線を検出できませんでした。"));
        }

        if (string.IsNullOrWhiteSpace(raceName))
        {
            issues.Add(new JraPageParseIssue(
                "race_card.race_name_missing",
                JraPageDiagnosticSeverity.Error,
                "出馬表ページからレース名を検出できませんでした。"));
        }

        var data = new JraRaceCardPage(
            snapshot.Url,
            raceName,
            raceDate,
            racecourse,
            distance,
            nextLinks.Select(link => link.Label).ToList(),
            issues);

        var confidence = !string.IsNullOrWhiteSpace(raceName) && nextLinks.Count > 0
            ? JraPageParseConfidence.High
            : !string.IsNullOrWhiteSpace(raceName)
                ? JraPageParseConfidence.Medium
                : JraPageParseConfidence.Low;

        return new JraStructuredPageParseResult<JraRaceCardPage>(
            !string.IsNullOrWhiteSpace(raceName),
            data,
            issues,
            confidence,
            nextLinks,
            !string.IsNullOrWhiteSpace(raceName) ? null : "出馬表ページの解析に失敗しました。");
    }

    private static bool IsRaceHeading(string heading)
        => !string.IsNullOrWhiteSpace(heading)
            && IgnoredHeadings.All(ignored => !JraPageParserText.ContainsNormalized(heading, ignored));

    private static IEnumerable<JraStructuredPageNextLink> BuildImplicitRacePageLinks(string sourceUrl)
    {
        var oddsUrl = ConvertAccessRacePageUrl(sourceUrl, "accessO.html");
        if (!string.IsNullOrWhiteSpace(oddsUrl))
        {
            yield return new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenOdds,
                "オッズ",
                oddsUrl,
                JraStructuredLinkNavigationMode.DirectUrl);
        }

        var resultUrl = ConvertAccessRacePageUrl(sourceUrl, "accessP.html");
        if (!string.IsNullOrWhiteSpace(resultUrl))
        {
            yield return new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenResult,
                "払戻金",
                resultUrl,
                JraStructuredLinkNavigationMode.DirectUrl);
        }
    }

    private static string? ConvertAccessRacePageUrl(string sourceUrl, string targetFile)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl)
            || !sourceUrl.Contains("accessD.html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return sourceUrl.Replace("accessD.html", targetFile, StringComparison.OrdinalIgnoreCase);
    }

    private static string MapRelation(string label)
    {
        if (JraPageParserText.ContainsNormalized(label, "オッズ"))
        {
            return JraStructuredLinkRelations.OpenOdds;
        }

        if (JraPageParserText.ContainsNormalized(label, "払戻金")
            || JraPageParserText.ContainsNormalized(label, "レース結果"))
        {
            return JraStructuredLinkRelations.OpenResult;
        }

        if (JraPageParserText.ContainsNormalized(label, "出走馬情報"))
        {
            return JraStructuredLinkRelations.OpenHorseInfo;
        }

        if (JraPageParserText.ContainsNormalized(label, "データ分析"))
        {
            return JraStructuredLinkRelations.OpenData;
        }

        if (JraPageParserText.ContainsNormalized(label, "出馬表"))
        {
            return JraStructuredLinkRelations.OpenRaceCard;
        }

        return JraStructuredLinkRelations.OpenRelated;
    }
}