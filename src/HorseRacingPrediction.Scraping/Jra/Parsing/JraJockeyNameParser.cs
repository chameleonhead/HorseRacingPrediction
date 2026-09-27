using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

internal static class JraJockeyNameParser
{
    internal static void EnsureCompleteSnapshot(HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot source, JraPageKind kind)
    {
        if (source.Diagnostics.Any(item => item.Code == "table-cell-fragments-truncated"))
            throw new JraPageStructureException(kind, source.Url.ToString(),
                "セルのDOM情報が切り詰められており、騎手名の一意性を確認できません。", "JockeyName");
    }

    internal static string? Parse(JraCellView? cell, string header, string url, JraPageKind kind)
    {
        if (cell is null || string.IsNullOrWhiteSpace(cell.Text)) return null;
        var names = cell.Fragments.Where(fragment =>
            fragment.TagName.Equals("p", StringComparison.OrdinalIgnoreCase)
            && fragment.ClassTokens.Contains("jockey", StringComparer.Ordinal)).ToArray();
        string? name;
        if (names.Length == 1)
            name = names[0].Text;
        else if (names.Length == 0 && header.Trim() is "騎手" or "騎手名")
            name = cell.Text; // The entire cell is a dedicated name field, not a combined card cell.
        else
            throw new JraPageStructureException(kind, url,
                "騎手名のDOM要素を一意に確認できません。", "JockeyName");
        var normalized = JockeyNameNormalizer.Normalize(name ?? string.Empty);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
