using System.Globalization;
using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

public sealed class JraThisWeekFeatureParser : IJraStructuredPageParser<JraThisWeekPage>
{
    private static readonly HashSet<string> RelatedLinkLabels =
    [
        "レーストップ",
        "出馬表",
        "出走馬情報",
        "調教動画ほか",
        "データ分析",
        "プレレーティング",
        "プレイバック",
    ];

    public JraStructuredPageParseResult<JraThisWeekPage> Parse(PageSnapshot snapshot)
    {
        var issues = new List<JraPageParseIssue>();
        var dateRangeLabel = snapshot.MainText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Contains("月", StringComparison.Ordinal)
                && line.Contains("日", StringComparison.Ordinal)
                && (line.Contains("～", StringComparison.Ordinal) || line.Contains("-", StringComparison.Ordinal)));

        var featuredRaces = new List<JraThisWeekRaceEntry>();
        JraThisWeekRaceEntry? current = null;

        foreach (var link in snapshot.Links)
        {
            if (TryParseFeaturedRaceHeader(link.Title, out var raceDate, out var raceName, out var grade, out var racecourse, out var distance))
            {
                if (current is not null)
                {
                    featuredRaces.Add(current);
                }

                current = new JraThisWeekRaceEntry(
                    raceDate,
                    raceName,
                    grade,
                    racecourse,
                    distance,
                    JraPageParserText.ResolveUrl(snapshot.Url, link.Url),
                    null,
                    null,
                    null,
                    null,
                    null);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (!RelatedLinkLabels.Any(label => JraPageParserText.ContainsNormalized(link.Title, label)))
            {
                continue;
            }

            var resolvedUrl = JraPageParserText.ResolveUrl(snapshot.Url, link.Url);
            current = current with
            {
                SpecialPageUrl = JraPageParserText.ContainsNormalized(link.Title, "レーストップ") ? resolvedUrl : current.SpecialPageUrl,
                RaceCardUrl = JraPageParserText.ContainsNormalized(link.Title, "出馬表") ? resolvedUrl : current.RaceCardUrl,
                HorseInfoUrl = JraPageParserText.ContainsNormalized(link.Title, "出走馬情報") ? resolvedUrl : current.HorseInfoUrl,
                DataUrl = JraPageParserText.ContainsNormalized(link.Title, "データ分析") ? resolvedUrl : current.DataUrl,
                RatingUrl = JraPageParserText.ContainsNormalized(link.Title, "プレレーティング") ? resolvedUrl : current.RatingUrl,
                PlaybackUrl = JraPageParserText.ContainsNormalized(link.Title, "プレイバック") ? resolvedUrl : current.PlaybackUrl,
            };
        }

        if (current is not null)
        {
            featuredRaces.Add(current);
        }

        if (featuredRaces.Count == 0)
        {
            issues.Add(new JraPageParseIssue(
                "thisweek.featured_races_missing",
                JraPageDiagnosticSeverity.Warning,
                "今週の注目レースページから注目レース導線を検出できませんでした。"));
        }

        var nextLinks = featuredRaces
            .SelectMany(BuildThisWeekNextLinks)
            .ToList();

        var data = new JraThisWeekPage(snapshot.Url, dateRangeLabel, featuredRaces, issues);
        return new JraStructuredPageParseResult<JraThisWeekPage>(
            featuredRaces.Count > 0,
            data,
            issues,
            featuredRaces.Count > 0 ? JraPageParseConfidence.High : JraPageParseConfidence.Low,
            nextLinks,
            featuredRaces.Count > 0 ? null : "今週の注目レースページの解析に失敗しました。");
    }

    private static IEnumerable<JraStructuredPageNextLink> BuildThisWeekNextLinks(JraThisWeekRaceEntry race)
    {
        if (!string.IsNullOrWhiteSpace(race.SpecialPageUrl))
        {
            yield return new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenSpecial(race.RaceName),
                $"{race.RaceName} レーストップ",
                race.SpecialPageUrl,
                JraPageParserText.InferNavigationMode(race.SpecialPageUrl));
        }

        if (!string.IsNullOrWhiteSpace(race.RaceCardUrl))
        {
            yield return new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenRaceCardForRace(race.RaceName),
                $"{race.RaceName} 出馬表",
                race.RaceCardUrl,
                JraPageParserText.InferNavigationMode(race.RaceCardUrl));
        }

        if (!string.IsNullOrWhiteSpace(race.HorseInfoUrl))
        {
            yield return new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenHorseInfoForRace(race.RaceName),
                $"{race.RaceName} 出走馬情報",
                race.HorseInfoUrl,
                JraPageParserText.InferNavigationMode(race.HorseInfoUrl));
        }

        if (!string.IsNullOrWhiteSpace(race.DataUrl))
        {
            yield return new JraStructuredPageNextLink(
                JraStructuredLinkRelations.OpenData,
                $"{race.RaceName} データ分析",
                race.DataUrl,
                JraPageParserText.InferNavigationMode(race.DataUrl));
        }
    }

    private static bool TryParseFeaturedRaceHeader(
        string title,
        out DateOnly? raceDate,
        out string raceName,
        out string? grade,
        out string? racecourse,
        out string? distance)
    {
        raceDate = null;
        raceName = string.Empty;
        grade = null;
        racecourse = null;
        distance = null;

        var match = JraPageParserText.FeaturedRaceHeaderRegex.Match(title ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        if (int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            && int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            var year = DateTime.Today.Year;
            try
            {
                raceDate = new DateOnly(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                raceDate = null;
            }
        }

        raceName = match.Groups["raceName"].Value.Trim();
        grade = string.IsNullOrWhiteSpace(match.Groups["grade"].Value) ? null : match.Groups["grade"].Value.Trim();
        racecourse = string.IsNullOrWhiteSpace(match.Groups["racecourse"].Value) ? null : match.Groups["racecourse"].Value.Trim();
        distance = string.IsNullOrWhiteSpace(match.Groups["distance"].Value) ? null : match.Groups["distance"].Value.Trim();
        return true;
    }
}