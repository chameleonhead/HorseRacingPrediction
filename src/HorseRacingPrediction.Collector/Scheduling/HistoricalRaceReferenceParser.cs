using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Collector.Scheduling;

public static class HistoricalRaceReferenceParser
{
    private static readonly string[] DateHeaders = ["年月日", "日付", "開催日", "年月日・開催"];
    private static readonly string[] RacecourseHeaders = ["開催", "競馬場", "場名", "場所", "場"];
    private static readonly string[] RaceNumberHeaders = ["R", "レース", "レース番号"];

    private static readonly string[] RacecourseNames =
    [
        "札幌",
        "函館",
        "福島",
        "新潟",
        "東京",
        "中山",
        "中京",
        "京都",
        "阪神",
        "小倉"
    ];

    public static IReadOnlyList<HistoricalRaceReference> Parse(PageSnapshot snapshot, DateOnly currentRaceDate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var references = new List<HistoricalRaceReference>();

        foreach (var table in snapshot.Tables)
        {
            var dateIndex = FindHeaderIndex(table.Headers, DateHeaders);
            var racecourseIndex = FindHeaderIndex(table.Headers, RacecourseHeaders);
            var raceNumberIndex = FindHeaderIndex(table.Headers, RaceNumberHeaders);

            foreach (var row in table.Rows)
            {
                if (TryParseReference(row, dateIndex, racecourseIndex, raceNumberIndex, currentRaceDate) is not { } reference)
                {
                    continue;
                }

                references.Add(reference);
            }
        }

        return references
            .DistinctBy(x => $"{x.RaceDate:yyyy-MM-dd}|{x.Racecourse}|{x.RaceNumber:D2}")
            .ToList();
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, params string[] candidates)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            var header = Normalize(headers[index]);
            if (candidates.Any(candidate => header.Contains(Normalize(candidate), StringComparison.Ordinal)))
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetCell(IReadOnlyList<string> row, int index)
    {
        if (index < 0 || index >= row.Count)
        {
            return string.Empty;
        }

        return row[index].Trim();
    }

    private static HistoricalRaceReference? TryParseReference(
        IReadOnlyList<string> row,
        int dateIndex,
        int racecourseIndex,
        int raceNumberIndex,
        DateOnly currentRaceDate)
    {
        var raceDate = ParseRaceDate(GetCell(row, dateIndex), currentRaceDate);
        var racecourse = ParseRacecourse(GetCell(row, racecourseIndex));
        var raceNumber = ParseRaceNumber(GetCell(row, raceNumberIndex));

        if (raceDate is not null && !string.IsNullOrWhiteSpace(racecourse) && raceNumber is not null)
        {
            return new HistoricalRaceReference(raceDate.Value, racecourse, raceNumber.Value);
        }

        var combinedText = string.Join(' ', row.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(combinedText))
        {
            return null;
        }

        raceDate ??= ParseRaceDate(combinedText, currentRaceDate);
        racecourse ??= ParseRacecourse(combinedText);
        raceNumber ??= ParseRaceNumber(combinedText);

        return raceDate is not null && !string.IsNullOrWhiteSpace(racecourse) && raceNumber is not null
            ? new HistoricalRaceReference(raceDate.Value, racecourse, raceNumber.Value)
            : null;
    }

    private static DateOnly? ParseRaceDate(string raw, DateOnly currentRaceDate)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var fullDateMatch = Regex.Match(raw, @"(?<year>\d{4})[./年-](?<month>\d{1,2})[./月-](?<day>\d{1,2})");
        if (fullDateMatch.Success)
        {
            return CreateDate(
                fullDateMatch.Groups["year"].Value,
                fullDateMatch.Groups["month"].Value,
                fullDateMatch.Groups["day"].Value);
        }

        var shortDateMatch = Regex.Match(raw, @"(?<month>\d{1,2})[./月](?<day>\d{1,2})");
        if (!shortDateMatch.Success)
        {
            return null;
        }

        if (!int.TryParse(shortDateMatch.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(shortDateMatch.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        var year = currentRaceDate.Year;
        if (month > currentRaceDate.Month || (month == currentRaceDate.Month && day > currentRaceDate.Day))
        {
            year--;
        }

        return CreateDate(year.ToString(CultureInfo.InvariantCulture), month.ToString(CultureInfo.InvariantCulture), day.ToString(CultureInfo.InvariantCulture));
    }

    private static DateOnly? CreateDate(string yearText, string monthText, string dayText)
    {
        if (!int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(monthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(dayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? ParseRacecourse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return RacecourseNames.FirstOrDefault(raw.Contains);
    }

    private static int? ParseRaceNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = Regex.Match(raw, @"(?<number>\d{1,2})\s*R", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(raw, @"第(?<number>\d{1,2})レース");
        }

        if (!match.Success)
        {
            match = Regex.Match(raw, @"^(?<number>\d{1,2})$");
        }

        return match.Success && int.TryParse(match.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raceNumber)
            ? raceNumber
            : null;
    }

    private static string Normalize(string value)
        => value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("・", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}