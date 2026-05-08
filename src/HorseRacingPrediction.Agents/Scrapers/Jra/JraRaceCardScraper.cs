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
        var snapshot = await _browser.GetPageSnapshotAsync(1, cancellationToken);

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

    /// <summary>
    /// ブラウザが既に出馬表ページを表示している状態でページを解析する。
    /// クリックで遷移した直後に呼び出すことで URL ナビゲーションを省略できる。
    /// </summary>
    public async Task<JraRaceCardData?> ScrapeCurrentPageAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _browser.GetPageSnapshotAsync(1, cancellationToken);
        var url = _browser.CurrentUrl ?? string.Empty;

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
        var racecourse = ExtractRacecourse(snapshot) ?? ExtractRacecourse(searchText);
        var raceDate = ExtractDate(searchText);
        var raceNumber = ExtractRaceNumber(searchText);
        var courseType = ExtractCourseType(searchText);
        var distance = ExtractDistance(searchText);
        var grade = ExtractGrade(searchText);

        return new RaceMetadata(raceName, racecourse, raceDate, raceNumber, courseType, distance, grade);
    }

    private static string ExtractRaceName(PageSnapshot snapshot)
    {
        var headingRaceName = snapshot.Headings
            .Select(CleanRaceName)
            .FirstOrDefault(IsLikelyRaceName);
        if (!string.IsNullOrWhiteSpace(headingRaceName))
        {
            return headingRaceName;
        }

        var combinedText = string.Join("\n", snapshot.Headings.Append(snapshot.MainText));
        var raceLikeMatch = Regex.Match(
            combinedText,
            @"(?:第\d+回\s*)?(?<name>[^\s\r\n]+?)\s+G[ⅠⅡⅢ1-3]",
            RegexOptions.CultureInvariant);
        if (raceLikeMatch.Success && IsLikelyRaceName(raceLikeMatch.Groups["name"].Value))
        {
            return CleanRaceName(raceLikeMatch.Groups["name"].Value);
        }

        var namedRaceMatch = Regex.Match(
            combinedText,
            @"(?:第\d+回\s*)?(?<name>[^\s\r\n]+(?:記念|カップ|ステークス|賞|S))",
            RegexOptions.CultureInvariant);
        if (namedRaceMatch.Success && IsLikelyRaceName(namedRaceMatch.Groups["name"].Value))
        {
            return CleanRaceName(namedRaceMatch.Groups["name"].Value);
        }

        var candidates = snapshot.Headings
            .Select(h => h.Trim())
            .Where(h => h.Length > 1)
            .Where(h => !IsBoilerplateHeading(h))
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

    private static string? ExtractRacecourse(PageSnapshot snapshot)
    {
        var source = string.Join("\n", snapshot.Headings.Append(snapshot.MainText));
        var match = Regex.Match(source, @"\d+回(?<racecourse>東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)\d+日");
        return match.Success ? match.Groups["racecourse"].Value : null;
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

    private static bool IsBoilerplateHeading(string text)
        => text.Contains("JRA 日本中央競馬会", StringComparison.Ordinal)
           || text.StartsWith("出馬表", StringComparison.Ordinal)
           || text.Contains("検索ウィンドウ", StringComparison.Ordinal)
           || text.Contains("競馬メニュー", StringComparison.Ordinal)
           || text.Contains("ニュース", StringComparison.Ordinal);

    private static bool IsLikelyRaceName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = CleanRaceName(text);
        if (string.IsNullOrWhiteSpace(cleaned)
            || IsBoilerplateHeading(cleaned)
            || cleaned.StartsWith("本賞", StringComparison.Ordinal)
            || cleaned.StartsWith("付加賞", StringComparison.Ordinal)
            || cleaned == "更新")
        {
            return false;
        }

        return cleaned.EndsWith("記念", StringComparison.Ordinal)
            || cleaned.EndsWith("カップ", StringComparison.Ordinal)
            || cleaned.EndsWith("ステークス", StringComparison.Ordinal)
            || cleaned.EndsWith("新聞杯", StringComparison.Ordinal)
            || cleaned.EndsWith("賞", StringComparison.Ordinal)
            || cleaned.EndsWith("S", StringComparison.Ordinal);
    }

    private static string CleanRaceName(string name)
    {
        name = Regex.Replace(name, @"^第\d+回\s*", string.Empty, RegexOptions.CultureInvariant).Trim();
        name = Regex.Replace(name, @"\s+G[ⅠⅡⅢ1-3]$", string.Empty, RegexOptions.CultureInvariant).Trim();

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

        var gateNumberIndex = FindHeaderIndex(headers, "枠番", "枠");
        var jockeyIndex = FindHeaderIndex(headers, "騎手");
        var weightIndex = FindHeaderIndex(headers, "斤量", "負担重量");
        var sexAgeIndex = FindHeaderIndex(headers, "性齢");
        var bodyWeightIndex = FindHeaderIndex(headers, "馬体重");
        var trainerIndex = FindHeaderIndex(headers, "厩舎", "調教師");
        var ownerIndex = FindHeaderIndex(headers, "馬主");

        // JRA の出馬表は複合列ヘッダを持つことがある。
        // horseNameIndex と同一列になった場合は -1 に下げてセル内容から個別に抽出する。
        if (trainerIndex == horseNameIndex) trainerIndex = -1;
        if (bodyWeightIndex == horseNameIndex) bodyWeightIndex = -1;
        if (ownerIndex == horseNameIndex) ownerIndex = -1;
        if (weightIndex == horseNameIndex) weightIndex = -1;
        if (sexAgeIndex == horseNameIndex) sexAgeIndex = -1;

        // 騎手列が複合列 (性齢/毛色 負担重量 騎手名) かどうか判定
        var jockeyCellIsCombined = jockeyIndex >= 0
            && (headers[jockeyIndex].Contains("性齢", StringComparison.OrdinalIgnoreCase)
                || headers[jockeyIndex].Contains("負担重量", StringComparison.OrdinalIgnoreCase));

        // 複合騎手列が個別列の代わりになっている場合はそちらも -1 に下げて複合列で解決する
        if (jockeyCellIsCombined)
        {
            if (sexAgeIndex < 0 || sexAgeIndex == jockeyIndex) sexAgeIndex = jockeyIndex;
            if (weightIndex < 0 || weightIndex == jockeyIndex) weightIndex = jockeyIndex;
        }

        var entries = new List<JraRaceEntryData>();
        foreach (var row in table.Rows)
        {
            if (row.Count == 0)
            {
                continue;
            }

            var horseCellText = GetCell(row, horseNameIndex)?.Trim();
            if (string.IsNullOrWhiteSpace(horseCellText))
            {
                continue;
            }

            var horseNumberStr = GetCell(row, horseNumberIndex);
            var horseNumber = ParseInt(horseNumberStr);
            if (horseNumber is null or <= 0)
            {
                continue;
            }

            // 複合列から個別フィールドを抽出
            var horseName = ExtractHorseName(horseCellText);
            var trainerName = trainerIndex >= 0
                ? NullIfEmpty(GetCell(row, trainerIndex))
                : ExtractTrainerFromHorseCell(horseCellText);

            var jockeyCellText = GetCell(row, jockeyIndex)?.Trim();
            string? jockeyName;
            string? sexAge;
            decimal? weight;
            if (jockeyCellIsCombined && jockeyCellText is not null)
            {
                jockeyName = ExtractJockeyFromCombinedCell(jockeyCellText);
                sexAge = NullIfEmpty(ExtractSexAgeFromCombinedCell(jockeyCellText));
                weight = ExtractWeightFromCombinedCell(jockeyCellText);
            }
            else
            {
                jockeyName = NullIfEmpty(jockeyCellText);
                sexAge = NullIfEmpty(GetCell(row, sexAgeIndex));
                weight = ParseDecimal(GetCell(row, weightIndex));
            }

            var bodyWeightCell = GetCell(row, bodyWeightIndex);
            var (bodyWeight, bodyWeightDiff) = ParseBodyWeight(bodyWeightCell);

            entries.Add(new JraRaceEntryData(
                HorseNumber: horseNumber.Value,
                GateNumber: ParseInt(GetCell(row, gateNumberIndex)),
                HorseName: horseName,
                JockeyName: jockeyName,
                Weight: weight,
                SexAge: sexAge,
                BodyWeight: bodyWeight,
                BodyWeightDiff: bodyWeightDiff,
                TrainerName: trainerName,
                OwnerName: NullIfEmpty(GetCell(row, ownerIndex))));
        }

        return entries;
    }

    // ------------------------------------------------------------------ //
    // 複合セルからの個別フィールド抽出
    // ------------------------------------------------------------------ //

    // 馬名複合セル "デアトゥバトル (0.0.0.2) コウトミックレーシング..." → "デアトゥバトル"
    private static string ExtractHorseName(string cellText)
    {
        var first = cellText.TrimStart()
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return first ?? cellText.Trim();
    }

    // 馬名複合セルから調教師名を抽出: "水野 貴広(美浦)" → "水野 貴広"
    // JRA の出馬表では厩舎所属が (美浦)/(栗東)/(地方) の形で括弧内に入る
    private static readonly Regex TrainerInHorseCellRegex =
        new(@"([\p{L}]+\s+[\p{L}]+|[\p{L}]+)\((?:美浦|栗東|地方|JRA)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string? ExtractTrainerFromHorseCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return null;
        var m = TrainerInHorseCellRegex.Match(cellText);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // 複合騎手セル "牡3/黒鹿 57.0kg 松若 風馬" → "松若 風馬"
    private static string? ExtractJockeyFromCombinedCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return null;
        // "XXXkg " の後の文字列が騎手名
        var m = Regex.Match(cellText, @"\d+\.?\d*\s*kg\s+(.+)$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // 複合騎手セル "牡3/黒鹿 57.0kg ..." → "牡3"
    private static string? ExtractSexAgeFromCombinedCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return null;
        var m = Regex.Match(cellText, @"^([牡牝騸セ]\d+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    // 複合騎手セル "牡3/黒鹿 57.0kg ..." → 57.0
    private static decimal? ExtractWeightFromCombinedCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return null;
        var m = Regex.Match(cellText, @"(\d+\.?\d*)\s*kg", RegexOptions.IgnoreCase);
        return m.Success
            && decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var w)
            ? w : null;
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
