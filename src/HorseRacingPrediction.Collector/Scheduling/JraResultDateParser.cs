// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JraResultDateParser
{
    private static readonly Regex FullDateRegex = new(@"(?<year>\d{4})年(?<month>\d{1,2})月(?<day>\d{1,2})日", RegexOptions.Compiled);
    private static readonly Regex MonthDayRegex = new(@"(?<month>\d{1,2})月(?<day>\d{1,2})日", RegexOptions.Compiled);

    public IReadOnlyList<DateOnly> ParseMonthDates(PageSnapshot snapshot, int year, int month)
    {
        var targetMonth = new DateOnly(year, month, 1);
        var dates = new HashSet<DateOnly>();

        foreach (var link in snapshot.Links)
        {
            if (string.IsNullOrWhiteSpace(link.Url))
            {
                continue;
            }

            var parsed = JraRaceResultUrl.ParseFromUrl(link.Url);
            if (parsed.RaceDate is { } raceDate && raceDate.Year == year && raceDate.Month == month)
            {
                dates.Add(raceDate);
            }

            foreach (var value in ParseText(link.Title, targetMonth))
            {
                dates.Add(value);
            }
        }

        foreach (var text in EnumerateSnapshotText(snapshot))
        {
            foreach (var value in ParseText(text, targetMonth))
            {
                dates.Add(value);
            }
        }

        return dates.OrderBy(x => x).ToList();
    }

    private static IEnumerable<DateOnly> ParseText(string? text, DateOnly targetMonth)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in FullDateRegex.Matches(text))
        {
            if (!int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                || !int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                || !int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            {
                continue;
            }

            if (year == targetMonth.Year && month == targetMonth.Month && DateOnly.TryParse($"{year:D4}-{month:D2}-{day:D2}", out var value))
            {
                yield return value;
            }
        }

        foreach (Match match in MonthDayRegex.Matches(text))
        {
            if (!int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                || !int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            {
                continue;
            }

            if (month == targetMonth.Month && DateOnly.TryParse($"{targetMonth.Year:D4}-{month:D2}-{day:D2}", out var value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> EnumerateSnapshotText(PageSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Title))
        {
            yield return snapshot.Title;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.MainText))
        {
            yield return snapshot.MainText;
        }

        foreach (var heading in snapshot.Headings)
        {
            if (!string.IsNullOrWhiteSpace(heading))
            {
                yield return heading;
            }
        }
    }
}
#endif