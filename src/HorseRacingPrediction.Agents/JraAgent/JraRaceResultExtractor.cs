using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// JRA 払戻金・レース結果ページから <see cref="JraRaceResultSummary"/> を抽出する。
/// 着順テーブルと払戻金テーブルをそれぞれ解析する。
/// </summary>
public sealed class JraRaceResultExtractor : IPageExtractor
{
    private static readonly string[] RacecourseNames =
    [
        "東京", "中山", "阪神", "京都", "中京", "小倉", "函館", "福島", "新潟", "札幌"
    ];

    private static readonly Dictionary<string, string> RacecourseCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["01"] = "札幌",
        ["02"] = "函館",
        ["03"] = "福島",
        ["04"] = "新潟",
        ["05"] = "東京",
        ["06"] = "中山",
        ["07"] = "中京",
        ["08"] = "京都",
        ["09"] = "阪神",
        ["10"] = "小倉",
    };

    public JraPageKind[] SupportedPageKinds => [JraPageKind.Result];

    public async Task<object?> ExtractAsync(IWebBrowser browser, CancellationToken cancellationToken = default)
    {
        var snapshot = await browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: cancellationToken);
        var url = browser.CurrentUrl ?? string.Empty;
        return ParseResult(snapshot, url);
    }

    private static JraRaceResultSummary ParseResult(PageSnapshot snapshot, string url)
    {
        var metadataText = string.Join('\n',
            new[] { snapshot.Title ?? string.Empty, snapshot.MainText }
                .Concat(snapshot.Headings));
        var raceName = ExtractRaceName(snapshot, metadataText) ?? snapshot.Title?.Trim();
        var raceDate = ParseRaceDate(metadataText) ?? ParseRaceDateFromUrl(url);
        var racecourse = ParseRacecourse(metadataText) ?? ParseRacecourseFromUrl(url);
        var raceNumber = ParseRaceNumber(metadataText) ?? ParseRaceNumberFromUrl(url);
        var gradeCode = ParseGradeCode(metadataText);
        var surfaceCode = ParseSurfaceCode(metadataText);
        var distanceMeters = ParseDistanceMeters(metadataText);
        var directionCode = ParseDirectionCode(metadataText);
        var entries  = new List<JraResultEntry>();
        var payouts  = new List<JraPayoutSummary>();

        foreach (var table in snapshot.Tables)
        {
            if (table.Headers.Count == 0) continue;
            var headers = table.Headers.Select(h => h.Trim()).ToList();

            // 着順テーブル
            var posIdx     = FindHeaderIndex(headers, "着順", "着");
            var gateNoIdx  = FindHeaderIndex(headers, "枠番", "枠");
            var horseNoIdx = FindHeaderIndex(headers, "馬番");
            var horseNmIdx = FindHeaderIndex(headers, "馬名");
            var sexAgeIdx  = FindHeaderIndex(headers, "性齢");
            var jockeyIdx  = FindHeaderIndex(headers, "騎手");
            var timeIdx    = FindHeaderIndex(headers, "タイム", "走破時計");
            var assignedWeightIdx = FindHeaderIndex(headers, "斤量", "負担重量", "負担体重");
            var bodyWeightIdx = FindHeaderIndex(headers, "馬体重");

            if (horseNoIdx >= 0 && horseNmIdx >= 0 && entries.Count == 0)
            {
                foreach (var row in table.Rows)
                {
                    var horseNoRaw = GetCell(row, horseNoIdx);
                    if (!int.TryParse(horseNoRaw, out var horseNo)) continue;

                    var (declaredWeight, declaredWeightDiff) = ParseBodyWeight(GetCell(row, bodyWeightIdx));

                    entries.Add(new JraResultEntry(
                        FinishPosition: ParseInt(GetCell(row, posIdx)),
                        HorseNumber: horseNo,
                        GateNumber: ParseInt(GetCell(row, gateNoIdx)),
                        HorseName: NullIfEmpty(GetCell(row, horseNmIdx)),
                        JockeyName: NullIfEmpty(GetCell(row, jockeyIdx)),
                        FinishTime: NullIfEmpty(GetCell(row, timeIdx)),
                        AssignedWeight: ParseDecimal(GetCell(row, assignedWeightIdx)),
                        SexAge: NullIfEmpty(GetCell(row, sexAgeIdx)),
                        DeclaredWeight: declaredWeight,
                        DeclaredWeightDiff: declaredWeightDiff));
                }
                continue;
            }

            // 払戻金テーブル（式別・組合せ・払戻金 の 3 列構造を目安にする）
            var betTypeIdx    = FindHeaderIndex(headers, "式別", "馬券", "賭式");
            var comboIdx      = FindHeaderIndex(headers, "組合せ", "馬番号");
            var payoutAmtIdx  = FindHeaderIndex(headers, "払戻金", "払戻");

            if (betTypeIdx >= 0 || (payoutAmtIdx >= 0 && payouts.Count == 0))
            {
                foreach (var row in table.Rows)
                {
                    var betType = NullIfEmpty(GetCell(row, betTypeIdx));
                    var combo   = NullIfEmpty(GetCell(row, comboIdx));
                    var payout  = NullIfEmpty(GetCell(row, payoutAmtIdx));

                    if (betType is null && combo is null && payout is null) continue;

                    payouts.Add(new JraPayoutSummary(
                        BetType: betType ?? string.Empty,
                        Combination: combo ?? string.Empty,
                        Payout: payout ?? string.Empty));
                }
            }
        }

        return new JraRaceResultSummary(
            RaceName: raceName,
            RaceDate: raceDate,
            Racecourse: racecourse,
            RaceNumber: raceNumber,
            GradeCode: gradeCode,
            SurfaceCode: surfaceCode,
            DistanceMeters: distanceMeters,
            DirectionCode: directionCode,
            Entries: entries,
            Payouts: payouts,
            SourceUrl: url);
    }

    private static string? ParseSurfaceCode(string text)
    {
        if (text.Contains("ダート", StringComparison.Ordinal)) return "ダート";
        if (text.Contains("芝", StringComparison.Ordinal)) return "芝";
        return null;
    }

    private static int? ParseDistanceMeters(string text)
    {
        var match = Regex.Match(
            text,
            @"(?<distance>\d{1,2},\d{3}|\d{3,4})\s*(?:[mMｍ]|メートル)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Groups["distance"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var distance)
            ? distance
            : null;
    }

    private static string? ParseDirectionCode(string text)
    {
        if (text.Contains("直線", StringComparison.Ordinal)) return "直線";
        if (text.Contains("右", StringComparison.Ordinal)) return "右";
        if (text.Contains("左", StringComparison.Ordinal)) return "左";
        return null;
    }

    private static string? ParseGradeCode(string text)
    {
        if (text.Contains("GⅠ", StringComparison.Ordinal) || text.Contains("G1", StringComparison.OrdinalIgnoreCase)) return "GⅠ";
        if (text.Contains("GⅡ", StringComparison.Ordinal) || text.Contains("G2", StringComparison.OrdinalIgnoreCase)) return "GⅡ";
        if (text.Contains("GⅢ", StringComparison.Ordinal) || text.Contains("G3", StringComparison.OrdinalIgnoreCase)) return "GⅢ";
        if (text.Contains("重賞", StringComparison.Ordinal)) return "重賞";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"[1１]勝クラス")) return "1勝クラス";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"[2２]勝クラス")) return "2勝クラス";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"[3３]勝クラス")) return "3勝クラス";
        if (text.Contains("オープン", StringComparison.Ordinal)) return "オープン";
        if (text.Contains("未勝利", StringComparison.Ordinal)) return "未勝利";
        if (text.Contains("新馬", StringComparison.Ordinal)) return "新馬";
        return null;
    }

    private static string? ExtractRaceName(PageSnapshot snapshot, string metadataText)
    {
        var candidates = snapshot.Headings
            .Select(x => x.Trim())
            .Where(x => x.Length > 1)
            .Where(x => !IsBoilerplateHeading(x))
            .Where(x => !IsDateRaceNumberLine(x))
            .Where(x => !IsCourseLine(x))
            .ToList();

        var headingName = candidates.FirstOrDefault(ContainsKanji);
        if (!string.IsNullOrWhiteSpace(headingName))
        {
            return headingName;
        }

        var classMatch = Regex.Match(
            metadataText,
            @"(?<name>\d+歳(?:以上|上)?(?:未勝利|新馬|未出走|[1１]勝クラス|[2２]勝クラス|[3３]勝クラス|オープン))");
        return classMatch.Success ? classMatch.Groups["name"].Value : null;
    }

    private static DateOnly? ParseRaceDate(string text)
    {
        var match = Regex.Match(text, @"(?<y>\d{4})年(?<m>\d{1,2})月(?<d>\d{1,2})日");
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["y"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            && int.TryParse(match.Groups["m"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            && int.TryParse(match.Groups["d"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)
            ? new DateOnly(y, m, d)
            : null;
    }

    private static string? ParseRacecourse(string text)
        => RacecourseNames.FirstOrDefault(name => text.Contains(name, StringComparison.Ordinal));

    private static int? ParseRaceNumber(string text)
    {
        var match = Regex.Match(text, @"(?<num>\d{1,2})R\b");
        if (!match.Success)
        {
            match = Regex.Match(text, @"第(?<num>\d{1,2})レース");
        }

        if (!match.Success)
        {
            match = Regex.Match(text, @"(?<num>\d{1,2})レース");
        }

        return match.Success && int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)
            ? num
            : null;
    }

    private static DateOnly? ParseRaceDateFromUrl(string url)
    {
        var cname = ExtractCname(url);
        if (string.IsNullOrWhiteSpace(cname))
        {
            return null;
        }

        var match = Regex.Match(cname, @"(?<date>\d{8})$");
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["date"].Value;
        return DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static int? ParseRaceNumberFromUrl(string url)
    {
        var cname = ExtractCname(url);
        if (string.IsNullOrWhiteSpace(cname))
        {
            return null;
        }

        var match = Regex.Match(cname, @"(?<race>\d{2})\d{8}$");
        return match.Success && int.TryParse(match.Groups["race"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var race)
            ? race
            : null;
    }

    private static string? ParseRacecourseFromUrl(string url)
    {
        var cname = ExtractCname(url);
        if (string.IsNullOrWhiteSpace(cname))
        {
            return null;
        }

        var match = Regex.Match(cname, @"^pw01sde1(?<course>\d{3})", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups["course"].Value;
        var key = raw.Length >= 2 ? raw[^2..] : raw;
        return RacecourseCodeMap.TryGetValue(key, out var racecourse) ? racecourse : null;
    }

    private static string? ExtractCname(string url)
    {
        var match = Regex.Match(url, @"CNAME=(?<cname>[^&#]+)", RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups["cname"].Value) : null;
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, params string[] keywords)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var normalizedHeader = NormalizeHeader(headers[i]);
            if (keywords.Any(k => normalizedHeader.Contains(NormalizeHeader(k), StringComparison.Ordinal)))
                return i;
        }
        return -1;
    }

    private static string NormalizeHeader(string? value)
        => new string((value ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c) && c != '\u3000')
            .ToArray());

    private static string GetCell(IReadOnlyList<string> row, int index)
    {
        if (index < 0 || index >= row.Count) return string.Empty;
        return row[index].Trim();
    }

    private static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "---") return null;
        var normalized = new string(raw
            .Where(c => char.IsDigit(c) || c is '.' or '-' or '+')
            .ToArray());
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static (decimal? weight, decimal? diff) ParseBodyWeight(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "---")
        {
            return (null, null);
        }

        var trimmed = raw.Trim();
        var openIdx = trimmed.IndexOf('(');
        if (openIdx < 0)
        {
            return (ParseDecimal(trimmed), null);
        }

        var closeIdx = trimmed.IndexOf(')', openIdx + 1);
        var weightPart = trimmed[..openIdx];
        var diffPart = closeIdx > openIdx
            ? trimmed.Substring(openIdx + 1, closeIdx - openIdx - 1)
            : trimmed[(openIdx + 1)..];

        return (ParseDecimal(weightPart), ParseDecimal(diffPart));
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool IsCourseLine(string text)
        => Regex.IsMatch(text, @"\d{1,2},\d{3}\s*(?:[mMｍ]|メートル)|\d{3,4}\s*(?:[mMｍ]|メートル)")
           || text.Contains("芝", StringComparison.Ordinal)
           || text.Contains("ダート", StringComparison.Ordinal);

    private static bool IsDateRaceNumberLine(string text)
        => (text.Contains("年", StringComparison.Ordinal)
            && text.Contains("月", StringComparison.Ordinal)
            && text.Contains("日", StringComparison.Ordinal))
           || Regex.IsMatch(text, @"\d{1,2}R\b")
           || Regex.IsMatch(text, @"\d{1,2}レース");

    private static bool IsBoilerplateHeading(string text)
        => text.Contains("JRA", StringComparison.OrdinalIgnoreCase)
           || text.Contains("レース結果", StringComparison.Ordinal)
           || text.Contains("開催選択", StringComparison.Ordinal)
           || text.Contains("レース選択", StringComparison.Ordinal)
           || text.Contains("日本中央競馬会", StringComparison.Ordinal);

    private static bool ContainsKanji(string text)
        => text.Any(c => c >= '\u4e00' && c <= '\u9fff');
}
