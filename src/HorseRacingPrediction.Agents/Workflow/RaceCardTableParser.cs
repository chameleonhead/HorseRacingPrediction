using System.Globalization;

namespace HorseRacingPrediction.Agents.Workflow;

internal static class RaceCardTableParser
{
    public static IReadOnlyList<RaceCardEntry> Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return [];

        var lines = markdown.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < lines.Length - 1; index++)
        {
            if (!IsTableRow(lines[index]) || !IsSeparatorRow(lines[index + 1]))
                continue;

            var headers = SplitCells(lines[index]);
            var horseNameIndex = FindHeader(headers, "馬名", "競走馬");
            if (horseNameIndex < 0)
                continue;

            var horseNumberIndex = FindHeader(headers, "馬番");
            if (horseNumberIndex < 0)
                continue;

            var gateNumberIndex = FindHeader(headers, "枠番");
            var jockeyIndex = FindHeader(headers, "騎手");
            var weightIndex = FindHeader(headers, "斤量");
            var sexAgeIndex = FindHeader(headers, "性齢");
            var bodyWeightIndex = FindHeader(headers, "馬体重", "馬体重(増減)");
            var trainerIndex = FindHeader(headers, "調教師", "厩舎");

            var entries = new List<RaceCardEntry>();
            for (var rowIndex = index + 2; rowIndex < lines.Length && IsTableRow(lines[rowIndex]); rowIndex++)
            {
                var cells = SplitCells(lines[rowIndex]);
                if (cells.Count != headers.Count)
                    continue;

                var horseName = cells[horseNameIndex].Trim();
                if (string.IsNullOrWhiteSpace(horseName))
                    continue;

                entries.Add(new RaceCardEntry(
                    ParseInt(cells[horseNumberIndex]) ?? 0,
                    gateNumberIndex >= 0 ? ParseInt(cells[gateNumberIndex]) : null,
                    horseName,
                    jockeyIndex >= 0 ? NullIfEmpty(cells[jockeyIndex]) : null,
                    weightIndex >= 0 ? ParseDecimal(cells[weightIndex]) : null,
                    ParseSexCode(sexAgeIndex >= 0 ? cells[sexAgeIndex] : null),
                    ParseAge(sexAgeIndex >= 0 ? cells[sexAgeIndex] : null),
                    bodyWeightIndex >= 0 ? ParseDeclaredWeight(cells[bodyWeightIndex]).weight : null,
                    bodyWeightIndex >= 0 ? ParseDeclaredWeight(cells[bodyWeightIndex]).diff : null,
                    trainerIndex >= 0 ? NullIfEmpty(cells[trainerIndex]) : null));
            }

            if (entries.Count > 0)
                return entries.Where(entry => entry.HorseNumber > 0).ToList();
        }

        return [];
    }

    private static bool IsTableRow(string line) => line.TrimStart().StartsWith('|');

    private static bool IsSeparatorRow(string line)
    {
        var cells = SplitCells(line);
        return cells.Count > 0 && cells.All(cell => cell.All(character => character is '-' or ':' or ' '));
    }

    private static List<string> SplitCells(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();

    private static int FindHeader(IReadOnlyList<string> headers, params string[] candidates)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (candidates.Any(candidate => headers[index].Contains(candidate, StringComparison.OrdinalIgnoreCase)))
                return index;
        }

        return -1;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = new string(value.Where(character => char.IsDigit(character) || character is '.' or '-' or '+').ToArray());
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? ParseSexCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim()[0].ToString();
    }

    private static int? ParseAge(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static (decimal? weight, decimal? diff) ParseDeclaredWeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, null);

        var trimmed = value.Trim();
        var open = trimmed.IndexOf('(');
        var close = trimmed.IndexOf(')');
        if (open > 0 && close > open)
        {
            var weight = ParseDecimal(trimmed[..open]);
            var diff = ParseDecimal(trimmed[(open + 1)..close]);
            return (weight, diff);
        }

        return (ParseDecimal(trimmed), null);
    }
}