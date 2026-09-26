using System.Text;
using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

internal static class RaceGrade
{
    public static string? Parse(JraSnapshotView snapshot, string? raceName = null)
    {
        // The selected race heading includes its grade icon's accessible text.
        // Global images/body also contain past performances and unrelated races.
        var text = (raceName ?? string.Empty).Normalize(NormalizationForm.FormKC);
        var match = Regex.Match(text, @"(?:J[・.]?)?G\s*(III|II|I|[123])(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var grade = match.Groups[1].Value.ToUpperInvariant() switch { "III" => "3", "II" => "2", "I" => "1", var n => n };
        return (match.Value.StartsWith('J') ? "JG" : "G") + grade;
    }
}
