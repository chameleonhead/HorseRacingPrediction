using System.Text;
using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

internal static class RaceGrade
{
    public static string? Parse(JraSnapshotView snapshot)
    {
        var imageText = snapshot.Source.Images.SelectMany(image =>
            new[] { image.AltText, image.AccessibleName, image.Title })
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var text = (string.Join(" ", snapshot.Headings) + " " + string.Join(" ", imageText) + " " + snapshot.MainText)
            .Normalize(NormalizationForm.FormKC);
        var match = Regex.Match(text, @"(?:J[・.]?)?G\s*(III|II|I|[123])(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var grade = match.Groups[1].Value.ToUpperInvariant() switch { "III" => "3", "II" => "2", "I" => "1", var n => n };
        return (match.Value.StartsWith('J') ? "JG" : "G") + grade;
    }
}
