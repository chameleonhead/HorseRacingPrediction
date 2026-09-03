using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>
/// JRA出馬表ページを解析する。馬番列を持つテーブルを対象とする。
/// 実ページの具体的なURL構造は未調査のため、テーブルの見出し（ヘッダー）から
/// 列を特定する方式にして、ページ構造変更への耐性を優先している。
/// </summary>
internal sealed class RaceCardPageParser
    : IJraPageParser
{
    private static readonly Regex DateRegex =
        new(@"(?<year>\d{4})年\s*(?<month>\d{1,2})月\s*(?<day>\d{1,2})日", RegexOptions.Compiled);

    private static readonly Regex RaceNumberRegex =
        new(@"(?<num>\d{1,2})\s*R", RegexOptions.Compiled);

    private static readonly Regex TimeRegex =
        new(@"(?<hour>\d{1,2}):(?<minute>\d{2})", RegexOptions.Compiled);

    private static readonly Regex LeadingNumberRegex =
        new(@"(?<num>\d{1,2})", RegexOptions.Compiled);

    private static readonly Regex WeightRegex =
        new(@"(?<weight>\d{1,3}(\.\d)?)", RegexOptions.Compiled);

    public JraPageKind Kind =>
        JraPageKind.RaceCard;

    public int Priority => 90;

    public bool CanParse(
        PageSnapshot snapshot)
    {
        return FindEntryTable(snapshot) is not null;
    }

    public IJraPage Parse(
        PageSnapshot snapshot)
    {
        var table =
            FindEntryTable(snapshot)
            ?? throw new JraPageParseException(
                JraPageKind.RaceCard,
                snapshot.Url,
                "出馬表テーブルを取得できませんでした。");

        var date =
            ParseDate(snapshot);

        var course =
            ParseCourse(snapshot);

        var number =
            ParseRaceNumber(snapshot);

        var raceId =
            new RaceId(date, course, number);

        var raceName =
            ParseRaceName(snapshot);

        var startTime =
            ParseStartTime(snapshot);

        var entries =
            ParseEntries(table);

        return new JraRaceCardPage(
            snapshot.Url,
            raceId,
            raceName,
            startTime,
            entries);
    }

    private static PageTableSnapshot? FindEntryTable(
        PageSnapshot snapshot)
    {
        foreach (var table in snapshot.Tables)
        {
            if (FindHorseNumberColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            if (FindHorseNameColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            // 着順列を持つ場合はレース結果テーブルであり、出馬表ではない。
            if (table.Headers.Any(h => h.Contains("着順", StringComparison.Ordinal)))
            {
                continue;
            }

            return table;
        }

        return null;
    }

    private static int FindHorseNumberColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("馬番", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindFrameNumberColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("枠番", StringComparison.Ordinal) ||
                headers[i].Contains("枠", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindHorseNameColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("馬名", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindJockeyColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("騎手", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindAssignedWeightColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("斤量", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static DateOnly ParseDate(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var match =
            DateRegex.Match(searchText);

        if (!match.Success)
        {
            throw new JraPageParseException(
                JraPageKind.RaceCard,
                snapshot.Url,
                "対象日付を取得できませんでした。");
        }

        return new DateOnly(
            int.Parse(match.Groups["year"].Value),
            int.Parse(match.Groups["month"].Value),
            int.Parse(match.Groups["day"].Value));
    }

    private static RaceCourse ParseCourse(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var course =
            RaceCourseNames.Parse(searchText);

        if (course == RaceCourse.Unknown)
        {
            throw new JraPageParseException(
                JraPageKind.RaceCard,
                snapshot.Url,
                "対象競馬場を取得できませんでした。");
        }

        return course;
    }

    private static int ParseRaceNumber(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var match =
            RaceNumberRegex.Match(searchText);

        if (!match.Success)
        {
            throw new JraPageParseException(
                JraPageKind.RaceCard,
                snapshot.Url,
                "対象レース番号を取得できませんでした。");
        }

        return int.Parse(match.Groups["num"].Value);
    }

    private static string? ParseRaceName(
        PageSnapshot snapshot)
    {
        foreach (var heading in snapshot.Headings)
        {
            var withoutNumber =
                RaceNumberRegex.Replace(heading, string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(withoutNumber) &&
                RaceCourseNames.Parse(withoutNumber) == RaceCourse.Unknown &&
                !DateRegex.IsMatch(withoutNumber))
            {
                return withoutNumber;
            }
        }

        return null;
    }

    private static TimeOnly? ParseStartTime(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var match =
            TimeRegex.Match(searchText);

        if (!match.Success)
        {
            return null;
        }

        return new TimeOnly(
            int.Parse(match.Groups["hour"].Value),
            int.Parse(match.Groups["minute"].Value));
    }

    private static IReadOnlyList<RaceEntry> ParseEntries(
        PageTableSnapshot table)
    {
        var horseNumberIndex = FindHorseNumberColumnIndex(table.Headers);
        var frameNumberIndex = FindFrameNumberColumnIndex(table.Headers);
        var horseNameIndex = FindHorseNameColumnIndex(table.Headers);
        var jockeyIndex = FindJockeyColumnIndex(table.Headers);
        var assignedWeightIndex = FindAssignedWeightColumnIndex(table.Headers);

        var entries = new List<RaceEntry>();

        foreach (var row in table.Rows)
        {
            if (horseNumberIndex >= row.Count)
            {
                continue;
            }

            var numberMatch =
                LeadingNumberRegex.Match(row[horseNumberIndex]);

            if (!numberMatch.Success)
            {
                continue;
            }

            var horseNumber =
                int.Parse(numberMatch.Groups["num"].Value);

            if (horseNameIndex < 0 || horseNameIndex >= row.Count ||
                string.IsNullOrWhiteSpace(row[horseNameIndex]))
            {
                continue;
            }

            var horseName = row[horseNameIndex];

            int? frameNumber = null;

            if (frameNumberIndex >= 0 && frameNumberIndex < row.Count)
            {
                var frameMatch =
                    LeadingNumberRegex.Match(row[frameNumberIndex]);

                if (frameMatch.Success)
                {
                    frameNumber = int.Parse(frameMatch.Groups["num"].Value);
                }
            }

            var jockeyName =
                jockeyIndex >= 0 && jockeyIndex < row.Count && !string.IsNullOrWhiteSpace(row[jockeyIndex])
                    ? row[jockeyIndex]
                    : null;

            decimal? assignedWeight = null;

            if (assignedWeightIndex >= 0 && assignedWeightIndex < row.Count)
            {
                var weightMatch =
                    WeightRegex.Match(row[assignedWeightIndex]);

                if (weightMatch.Success)
                {
                    assignedWeight = decimal.Parse(weightMatch.Groups["weight"].Value);
                }
            }

            entries.Add(new RaceEntry(
                horseNumber,
                horseName,
                frameNumber,
                jockeyName,
                assignedWeight));
        }

        return entries;
    }
}
