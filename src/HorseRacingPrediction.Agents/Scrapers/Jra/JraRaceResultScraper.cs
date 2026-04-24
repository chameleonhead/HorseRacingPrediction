using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 公式サイトの成績ページから構造化データを抽出するスクレイパー。
/// <para>
/// AIエージェントが成績 URL を特定した後、このスクレイパーが Playwright でアクセスし、
/// ページ内のテーブル構造を解析して <see cref="JraRaceResultData"/> として返す。
/// </para>
/// <para>
/// 成績テーブル（着順・馬名・タイムなど）と払い戻しテーブル（単勝・複勝など）を
/// それぞれ独立して解析する。テーブルが取得できない場合はエントリや払い戻しを空のまま返す。
/// </para>
/// </summary>
public sealed class JraRaceResultScraper : IScraper<JraRaceResultData>
{
    private static readonly string[] RacecourseNames =
    [
        "東京", "中山", "阪神", "京都", "中京", "小倉", "函館", "福島", "新潟", "札幌"
    ];

    private static readonly string[] ResultTableRequiredHeaders = ["着順", "馬番", "馬名"];

    private static readonly string[] PayoutTableRequiredHeaders = ["払戻", "払い戻し", "式別"];

    private static readonly Dictionary<string, string> BetTypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["単勝"] = "win",
        ["複勝"] = "place",
        ["馬連"] = "quinella",
        ["ワイド"] = "wide",
        ["馬単"] = "exacta",
        ["三連複"] = "trio",
        ["三連単"] = "trifecta",
    };

    private readonly IWebBrowser _browser;

    public JraRaceResultScraper(IWebBrowser browser)
    {
        _browser = browser;
    }

    /// <inheritdoc />
    public async Task<JraRaceResultData?> ScrapeAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        await _browser.NavigateAsync(url, cancellationToken);
        var snapshot = await _browser.GetPageSnapshotAsync(0, cancellationToken);

        var metadata = ParseRaceMetadata(snapshot);
        var entries = ParseResultEntries(snapshot.Tables);
        var payouts = ParsePayouts(snapshot.MainText, snapshot.Tables);

        return new JraRaceResultData(
            Url: url,
            RaceName: metadata.RaceName,
            Racecourse: metadata.Racecourse,
            RaceDate: metadata.RaceDate,
            RaceNumber: metadata.RaceNumber,
            CourseType: metadata.CourseType,
            Distance: metadata.Distance,
            Grade: metadata.Grade,
            Entries: entries,
            Payouts: payouts);
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
        var parts = name.Split([' ', '\u3000', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? string.Join(" ", parts) : name;
    }

    // ------------------------------------------------------------------ //
    // 成績エントリの解析
    // ------------------------------------------------------------------ //

    private static IReadOnlyList<JraRaceResultEntryData> ParseResultEntries(
        IReadOnlyList<PageTableSnapshot> tables)
    {
        var resultTable = tables.FirstOrDefault(IsResultTable);
        return resultTable is null ? [] : ParseResultTable(resultTable);
    }

    private static bool IsResultTable(PageTableSnapshot table) =>
        table.Headers.Any(h =>
            ResultTableRequiredHeaders.Any(candidate =>
                h.Contains(candidate, StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<JraRaceResultEntryData> ParseResultTable(PageTableSnapshot table)
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

        var finishPositionIndex = FindHeaderIndex(headers, "着順", "着");
        var gateNumberIndex = FindHeaderIndex(headers, "枠番");
        var jockeyIndex = FindHeaderIndex(headers, "騎手");
        var weightIndex = FindHeaderIndex(headers, "斤量");
        var sexAgeIndex = FindHeaderIndex(headers, "性齢");
        var officialTimeIndex = FindHeaderIndex(headers, "タイム", "時間");
        var marginIndex = FindHeaderIndex(headers, "着差");
        var last3FIndex = FindHeaderIndex(headers, "後3F", "後半3F", "ﾗｽﾄ3F");
        var bodyWeightIndex = FindHeaderIndex(headers, "馬体重");
        var trainerIndex = FindHeaderIndex(headers, "厩舎", "調教師");

        var entries = new List<JraRaceResultEntryData>();
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

            var finishPositionCell = GetCell(row, finishPositionIndex);
            var (finishPosition, abnormalCode) = ParseFinishPosition(finishPositionCell);

            var bodyWeightCell = GetCell(row, bodyWeightIndex);
            var (bodyWeight, bodyWeightDiff) = ParseBodyWeight(bodyWeightCell);

            entries.Add(new JraRaceResultEntryData(
                FinishPosition: finishPosition,
                HorseNumber: horseNumber.Value,
                GateNumber: ParseInt(GetCell(row, gateNumberIndex)),
                HorseName: horseName,
                JockeyName: NullIfEmpty(GetCell(row, jockeyIndex)),
                Weight: ParseDecimal(GetCell(row, weightIndex)),
                SexAge: NullIfEmpty(GetCell(row, sexAgeIndex)),
                OfficialTime: NullIfEmpty(GetCell(row, officialTimeIndex)),
                MarginText: NullIfEmpty(GetCell(row, marginIndex)),
                LastThreeFurlongTime: NullIfEmpty(GetCell(row, last3FIndex)),
                BodyWeight: bodyWeight,
                BodyWeightDiff: bodyWeightDiff,
                TrainerName: NullIfEmpty(GetCell(row, trainerIndex)),
                AbnormalResultCode: abnormalCode));
        }

        return entries;
    }

    // ------------------------------------------------------------------ //
    // 払い戻しデータの解析
    // ------------------------------------------------------------------ //

    private static JraRacePayoutData? ParsePayouts(
        string mainText,
        IReadOnlyList<PageTableSnapshot> tables)
    {
        var payoutTable = tables.FirstOrDefault(IsPayoutTable);

        var win = new List<JraPayoutEntry>();
        var place = new List<JraPayoutEntry>();
        var quinella = new List<JraPayoutEntry>();
        var wide = new List<JraPayoutEntry>();
        var exacta = new List<JraPayoutEntry>();
        var trio = new List<JraPayoutEntry>();
        var trifecta = new List<JraPayoutEntry>();

        if (payoutTable is not null)
        {
            ParsePayoutTable(payoutTable, win, place, quinella, wide, exacta, trio, trifecta);
        }
        else
        {
            // テーブルが見つからない場合は本文テキストからパースを試みる
            ParsePayoutText(mainText, win, place, quinella, wide, exacta, trio, trifecta);
        }

        if (win.Count == 0 && place.Count == 0 && quinella.Count == 0 &&
            exacta.Count == 0 && trio.Count == 0 && trifecta.Count == 0)
        {
            return null;
        }

        return new JraRacePayoutData(
            WinPayouts: win,
            PlacePayouts: place,
            QuinellaPayouts: quinella,
            WidePayouts: wide,
            ExactaPayouts: exacta,
            TrioPayouts: trio,
            TrifectaPayouts: trifecta);
    }

    private static bool IsPayoutTable(PageTableSnapshot table) =>
        table.Headers.Any(h =>
            PayoutTableRequiredHeaders.Any(candidate =>
                h.Contains(candidate, StringComparison.OrdinalIgnoreCase)));

    private static void ParsePayoutTable(
        PageTableSnapshot table,
        List<JraPayoutEntry> win,
        List<JraPayoutEntry> place,
        List<JraPayoutEntry> quinella,
        List<JraPayoutEntry> wide,
        List<JraPayoutEntry> exacta,
        List<JraPayoutEntry> trio,
        List<JraPayoutEntry> trifecta)
    {
        // テーブルの列構成: 式別 | 馬番・組み合わせ | 払戻金
        // または: 単勝 | 馬番 | 金額 のように式別が列名になっている場合もある
        var headers = table.Headers;
        var betTypeIndex = FindHeaderIndex(headers, "式別", "賭式");
        var combinationIndex = FindHeaderIndex(headers, "馬番", "組み合わせ", "着順");
        var amountIndex = FindHeaderIndex(headers, "払戻金", "払戻", "配当");

        if (betTypeIndex < 0 || amountIndex < 0)
        {
            // 式別が列名になっているパターン（縦方向に式別が並ぶ）
            TryParseVerticalPayoutTable(table, win, place, quinella, wide, exacta, trio, trifecta);
            return;
        }

        // 式別・組み合わせ・払戻金が横に並ぶパターン
        var currentBetType = string.Empty;
        foreach (var row in table.Rows)
        {
            var betTypeCell = NullIfEmpty(GetCell(row, betTypeIndex));
            if (betTypeCell is not null)
            {
                currentBetType = betTypeCell;
            }

            var combination = combinationIndex >= 0
                ? NullIfEmpty(GetCell(row, combinationIndex))
                : null;
            var amountStr = NullIfEmpty(GetCell(row, amountIndex));
            var amount = ParsePayoutAmount(amountStr);

            if (combination is null || amount is null)
            {
                continue;
            }

            var entry = new JraPayoutEntry(combination, amount.Value);
            AddToPayoutList(currentBetType, entry, win, place, quinella, wide, exacta, trio, trifecta);
        }
    }

    private static void TryParseVerticalPayoutTable(
        PageTableSnapshot table,
        List<JraPayoutEntry> win,
        List<JraPayoutEntry> place,
        List<JraPayoutEntry> quinella,
        List<JraPayoutEntry> wide,
        List<JraPayoutEntry> exacta,
        List<JraPayoutEntry> trio,
        List<JraPayoutEntry> trifecta)
    {
        // ヘッダーに式別名が含まれる場合: 単勝 | 複勝 | ... という列が並ぶ
        foreach (var row in table.Rows)
        {
            if (row.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < table.Headers.Count && i < row.Count; i++)
            {
                var betType = table.Headers[i];
                if (!BetTypeAliases.ContainsKey(betType))
                {
                    continue;
                }

                // この列のセルが "馬番 金額" や "金額" 形式
                var cellText = row[i]?.Trim();
                if (string.IsNullOrWhiteSpace(cellText))
                {
                    continue;
                }

                var parts = cellText.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var amount = ParsePayoutAmount(parts[^1]);
                    if (amount is not null)
                    {
                        var combination = string.Join("-", parts[..^1]);
                        AddToPayoutList(betType, new JraPayoutEntry(combination, amount.Value),
                            win, place, quinella, wide, exacta, trio, trifecta);
                    }
                }
                else if (parts.Length == 1)
                {
                    var amount = ParsePayoutAmount(parts[0]);
                    if (amount is not null)
                    {
                        AddToPayoutList(betType, new JraPayoutEntry("?", amount.Value),
                            win, place, quinella, wide, exacta, trio, trifecta);
                    }
                }
            }
        }
    }

    private static void ParsePayoutText(
        string mainText,
        List<JraPayoutEntry> win,
        List<JraPayoutEntry> place,
        List<JraPayoutEntry> quinella,
        List<JraPayoutEntry> wide,
        List<JraPayoutEntry> exacta,
        List<JraPayoutEntry> trio,
        List<JraPayoutEntry> trifecta)
    {
        // 本文テキストから「単勝 3 430円」などのパターンを抽出
        // 式別名 半角スペース 馬番(組み合わせ) 半角スペース 金額
        var pattern = new Regex(
            @"(単勝|複勝|馬連|ワイド|馬単|三連複|三連単)\s+([\d\-]+)\s+([\d,]+)円?",
            RegexOptions.Multiline);

        foreach (Match match in pattern.Matches(mainText))
        {
            var betType = match.Groups[1].Value;
            var combination = match.Groups[2].Value;
            var amountStr = match.Groups[3].Value.Replace(",", string.Empty);

            if (!decimal.TryParse(amountStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                continue;
            }

            AddToPayoutList(betType, new JraPayoutEntry(combination, amount),
                win, place, quinella, wide, exacta, trio, trifecta);
        }
    }

    private static void AddToPayoutList(
        string betType,
        JraPayoutEntry entry,
        List<JraPayoutEntry> win,
        List<JraPayoutEntry> place,
        List<JraPayoutEntry> quinella,
        List<JraPayoutEntry> wide,
        List<JraPayoutEntry> exacta,
        List<JraPayoutEntry> trio,
        List<JraPayoutEntry> trifecta)
    {
        if (!BetTypeAliases.TryGetValue(betType, out var key))
        {
            return;
        }

        var target = key switch
        {
            "win" => win,
            "place" => place,
            "quinella" => quinella,
            "wide" => wide,
            "exacta" => exacta,
            "trio" => trio,
            "trifecta" => trifecta,
            _ => null,
        };

        target?.Add(entry);
    }

    // ------------------------------------------------------------------ //
    // ユーティリティ
    // ------------------------------------------------------------------ //

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

    private static (int? finishPosition, string? abnormalCode) ParseFinishPosition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var trimmed = value.Trim();

        // 異常コード: 中止, 取消, 除外, 降着, 失格 等
        if (trimmed.Any(c => !char.IsDigit(c) && c != ' '))
        {
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            var code = new string(trimmed.Where(c => !char.IsDigit(c) && c != ' ').ToArray());
            var pos = digits.Length > 0 &&
                int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
                    ? p
                    : (int?)null;
            return (pos, string.IsNullOrEmpty(code) ? null : code);
        }

        return (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
            ? position
            : null, null);
    }

    private static decimal? ParsePayoutAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return decimal.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
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
