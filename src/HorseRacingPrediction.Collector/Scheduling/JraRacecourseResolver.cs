namespace HorseRacingPrediction.Collector.Scheduling;

public static class JraRacecourseResolver
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["01"] = "札幌",
            ["02"] = "函館",
            ["03"] = "福島",
            ["04"] = "新潟",
            ["05"] = "東京",
            ["06"] = "中山",
            ["07"] = "中京",
            ["08"] = "京都",
            ["09"] = "阪神",
            ["10"] = "小倉",
            ["札幌"] = "札幌",
            ["函館"] = "函館",
            ["福島"] = "福島",
            ["新潟"] = "新潟",
            ["東京"] = "東京",
            ["中山"] = "中山",
            ["中京"] = "中京",
            ["京都"] = "京都",
            ["阪神"] = "阪神",
            ["小倉"] = "小倉",
        };

    public static string? ResolveDisplayName(string? racecourse)
    {
        if (string.IsNullOrWhiteSpace(racecourse))
        {
            return null;
        }

        var trimmed = racecourse.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical)
            ? canonical
            : Aliases.FirstOrDefault(x => trimmed.Contains(x.Value, StringComparison.Ordinal)).Value;
    }

    public static HorseRacingPrediction.Scraping.Scrapers.Jra.JraRaceResultUrl Normalize(
        HorseRacingPrediction.Scraping.Scrapers.Jra.JraRaceResultUrl url)
    {
        var racecourse = ResolveDisplayName(url.Racecourse)
            ?? ResolveDisplayName(url.RacecourseCode);

        return string.Equals(url.Racecourse, racecourse, StringComparison.Ordinal)
            ? url
            : url with { Racecourse = racecourse };
    }
}
