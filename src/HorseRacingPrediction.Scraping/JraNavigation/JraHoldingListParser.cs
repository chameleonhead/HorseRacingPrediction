using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed class JraHoldingListParser : IJraStructuredPageParser<JraHoldingListPage>
{
    public JraStructuredPageParseResult<JraHoldingListPage> Parse(PageSnapshot snapshot)
    {
        var issues = new List<JraPageParseIssue>();
        var holdings = JraPageParserText.ExtractHoldingEntries(snapshot).ToList();

        if (holdings.Count == 0)
        {
            issues.Add(new JraPageParseIssue(
                "holding.list.empty",
                JraPageDiagnosticSeverity.Warning,
                "開催一覧ページから holding ラベルを検出できませんでした。"));
        }

        var nextLinks = holdings
            .Select(holding => new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenHolding,
                holding.Label,
                null,
                JraStructuredLinkNavigationMode.CurrentSessionAction))
            .ToList();

        var data = new JraHoldingListPage(snapshot.Url, holdings, issues);
        return new JraStructuredPageParseResult<JraHoldingListPage>(
            holdings.Count > 0,
            data,
            issues,
            holdings.Count > 0 ? JraPageParseConfidence.High : JraPageParseConfidence.Low,
            nextLinks,
            holdings.Count > 0 ? null : "開催一覧ページに holding ラベルが見つかりませんでした。");
    }
}