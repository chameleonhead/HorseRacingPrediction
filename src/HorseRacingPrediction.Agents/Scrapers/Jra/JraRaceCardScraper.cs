using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 公式サイトの出馬表ページから構造化データを抽出するスクレイパー。
/// <para>
/// AIエージェントが検索・探索によって出馬表の URL を特定した後、
/// このスクレイパーがその URL に Playwright でアクセスし、
/// ページ内のテーブル構造を解析して <see cref="JraRaceCardData"/> として返す。
/// </para>
/// <para>
/// テーブルが取得できない場合（ページ構造変更・認証が必要なページなど）は
/// エントリを空のまま返す。
/// </para>
/// </summary>
public sealed class JraRaceCardScraper : IScraper<JraRaceCardData>
{
    private static readonly string[] RacecourseNames =
    [
        "東京", "中山", "阪神", "京都", "中京", "小倉", "函館", "福島", "新潟", "札幌"
    ];

    private static readonly string[] RaceCardRequiredHeaders = ["馬番", "馬名", "競走馬"];

    private readonly IWebBrowser _browser;

    public JraRaceCardScraper(IWebBrowser browser)
    {
        _browser = browser;
    }

    /// <inheritdoc />
    public async Task<JraRaceCardData?> ScrapeAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        await _browser.NavigateAsync(url, cancellationToken);
        var snapshot = await _browser.GetPageSnapshotAsync(0, cancellationToken);

        var metadata = ParseRaceMetadata(snapshot);
        var entries = ParseEntries(snapshot.Tables);

        return new JraRaceCardData(
            Url: url,
            RaceName: metadata.RaceName,
            Racecourse: metadata.Racecourse,
            RaceDate: metadata.RaceDate,
            RaceNumber: metadata.RaceNumber,
            CourseType: metadata.CourseType,
            Distance: metadata.Distance,
            Grade: metadata.Grade,
            Entries: entries);
    }

    // ------------------------------------------------------------------ //
    // メタ情報の解析
    // ------------------------------------------------------------------ //

    private static RaceMetadata ParseRaceMetadata(PageSnapshot snapshot)
    {
        var headingsText = string.Join("\n", snapshot.Headings);
        var searchText = $"{snapshot.Title}\n{headingsText}\n{snapshot.MainText}";

        var raceName = ExtractRaceName(snapshot);
        var racecourse = ExtractRacecourse(searchText);
        var raceDate = ExtractDate(searchText);
        var raceNumber = ExtractRaceNumber(searchText);
        var courseType = ExtractCourseType(searchText);
        var distance = ExtractDistance(searchText);
        var grade = ExtractGrade(searchText);

        return new RaceMetadata(raceName, racecourse, raceDate, raceNumber, courseType, distance, grade);
    }

    private static string ExtractRaceName(PageSnapshot snapshot)
    {
        var candidates = snapshot.Headings
            .Select(h => h.Trim())
            .Where(h => h.Length > 1)
            .Where(h => !IsCourseLine(h))
            .Where(h => !IsDateRaceNumberLine(h))
            .ToList();

        var raceName = candidates.FirstOrDefault(h => ContainsKanji(h))
            ?? snapshot.Title?.Trim()
            ?? string.Empty;

        return CleanRaceName(raceName);
    }

    private static string? ExtractRacecourse(string text)
    {
        return RacecourseNames.FirstOrDefault(rc => text.Contains(rc, StringComparison.Ordinal));
    }

    private static DateOnly? ExtractDate(string text)
    {
        var match = Regex.Match(text, @"(\d{4})年(\d{1,2})月(\d{1,2})日");
        if (!match.Success)
        {
            return null;
        }

        if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) &&
            int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) &&
            int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            try
            {
                return new DateOnly(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private static int? ExtractRaceNumber(string text)
    {
        var match = Regex.Match(text, @"(\d{1,2})R\b");
        if (!match.Success)
        {
            match = Regex.Match(text, @"第(\d{1,2})レース");
        }

        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)
            ? num
            : null;
    }

    private static string? ExtractCourseType(string text)
    {
        if (text.Contains("ダート", StringComparison.Ordinal))
        {
            return "ダート";
        }

        if (text.Contains("芝", StringComparison.Ordinal))
        {
            return "芝";
        }

        return null;
    }

    private static int? ExtractDistance(string text)
    {
        var match = Regex.Match(text, @"(\d{3,4})\s*[mM]");
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dist)
            ? dist
            : null;
    }

    private static string? ExtractGrade(string text)
    {
        if (text.Contains("GⅠ", StringComparison.Ordinal) || text.Contains("G1", StringComparison.Ordinal))
        {
            return "GⅠ";
        }

        if (text.Contains("GⅡ", StringComparison.Ordinal) || text.Contains("G2", StringComparison.Ordinal))
        {
            return "GⅡ";
        }

        if (text.Contains("GⅢ", StringComparison.Ordinal) || text.Contains("G3", StringComparison.Ordinal))
        {
            return "GⅢ";
        }

        if (text.Contains("重賞", StringComparison.Ordinal))
        {
            return "重賞";
        }

        return null;
    }

    private static bool IsCourseLine(string text) =>
        Regex.IsMatch(text, @"\d{3,4}\s*[mM]") ||
        text.Contains("芝", StringComparison.Ordinal) ||
        text.Contains("ダート", StringComparison.Ordinal);

    private static bool IsDateRaceNumberLine(string text) =>
        (text.Contains("年", StringComparison.Ordinal) &&
         text.Contains("月", StringComparison.Ordinal) &&
         text.Contains("日", StringComparison.Ordinal)) ||
        Regex.IsMatch(text, @"\d{1,2}R\b");

    private static bool ContainsKanji(string text) =>
        text.Any(c => c >= '\u4e00' && c <= '\u9fff');

    private static string CleanRaceName(string name)
    {
        // グレード表記（GⅠ等）が混入している場合はスペースで分割して先頭部分を使う
        var parts = name.Split([' ', '\u3000', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? string.Join(" ", parts) : name;
    }

    // ------------------------------------------------------------------ //
    // 出走馬エントリの解析
    // ------------------------------------------------------------------ //

    private static IReadOnlyList<JraRaceEntryData> ParseEntries(IReadOnlyList<PageTableSnapshot> tables)
    {
        var raceTable = tables.FirstOrDefault(IsRaceCardTable);
        return raceTable is null ? [] : ParseRaceCardTable(raceTable);
    }

    private static bool IsRaceCardTable(PageTableSnapshot table) =>
        table.Headers.Any(h =>
            RaceCardRequiredHeaders.Any(candidate =>
                h.Contains(candidate, StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<JraRaceEntryData> ParseRaceCardTable(PageTableSnapshot table)
    {
        var headers = table.Headers;

        var horseNameIndex = FindHeaderIndex(headers, "馬名", "競走馬");
        if (horseNameIndex < 0)
        {
            return [];
        }

        var horseNumberIndex = FindHeaderIndex(headers, "馬番");
        if (horseNumberIndex < 0)
        {
            return [];
        }

        var gateNumberIndex = FindHeaderIndex(headers, "枠番");
        var jockeyIndex = FindHeaderIndex(headers, "騎手");
        var weightIndex = FindHeaderIndex(headers, "斤量");
        var sexAgeIndex = FindHeaderIndex(headers, "性齢");
        var bodyWeightIndex = FindHeaderIndex(headers, "馬体重");
        var trainerIndex = FindHeaderIndex(headers, "厩舎", "調教師");
        var ownerIndex = FindHeaderIndex(headers, "馬主");

        var entries = new List<JraRaceEntryData>();
        foreach (var row in table.Rows)
        {
            if (row.Count == 0)
            {
                continue;
            }

            var horseName = GetCell(row, horseNameIndex)?.Trim();
            if (string.IsNullOrWhiteSpace(horseName))
            {
                continue;
            }

            var horseNumberStr = GetCell(row, horseNumberIndex);
            var horseNumber = ParseInt(horseNumberStr);
            if (horseNumber is null or <= 0)
            {
                continue;
            }

            var bodyWeightCell = GetCell(row, bodyWeightIndex);
            var (bodyWeight, bodyWeightDiff) = ParseBodyWeight(bodyWeightCell);

            entries.Add(new JraRaceEntryData(
                HorseNumber: horseNumber.Value,
                GateNumber: ParseInt(GetCell(row, gateNumberIndex)),
                HorseName: horseName,
                JockeyName: NullIfEmpty(GetCell(row, jockeyIndex)),
                Weight: ParseDecimal(GetCell(row, weightIndex)),
                SexAge: NullIfEmpty(GetCell(row, sexAgeIndex)),
                BodyWeight: bodyWeight,
                BodyWeightDiff: bodyWeightDiff,
                TrainerName: NullIfEmpty(GetCell(row, trainerIndex)),
                OwnerName: NullIfEmpty(GetCell(row, ownerIndex))));
        }

        return entries;
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, params string[] candidates)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (candidates.Any(c => headers[i].Contains(c, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Where(c => char.IsDigit(c) || c is '.' or '-' or '+')
            .ToArray());
        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static (decimal? weight, decimal? diff) ParseBodyWeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

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

    // ------------------------------------------------------------------ //
    // 内部レコード
    // ------------------------------------------------------------------ //

    private sealed record RaceMetadata(
        string RaceName,
        string? Racecourse,
        DateOnly? RaceDate,
        int? RaceNumber,
        string? CourseType,
        int? Distance,
        string? Grade);
}
