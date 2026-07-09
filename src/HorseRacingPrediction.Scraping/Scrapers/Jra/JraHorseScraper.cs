using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Scraping.Scrapers.Jra;

/// <summary>
/// JRA 公式サイトの競走馬情報ページ（accessU.html）から構造化データを抽出するスクレイパー。
/// <para>
/// プロフィール（馬名・性別・生年月日・血統など）は <see cref="JraProfileExtractor"/> のロジックを再利用し、
/// ページ内の「競走成績」テーブルから過去の出走履歴を追加で解析して <see cref="JraHorseProfileData"/> として返す。
/// </para>
/// </summary>
public sealed class JraHorseScraper : IScraper<JraHorseProfileData>
{
    private static readonly string[] RacecourseNames =
    [
        "東京", "中山", "阪神", "京都", "中京", "小倉", "函館", "福島", "新潟", "札幌"
    ];

    private readonly IWebBrowser _browser;

    public JraHorseScraper(IWebBrowser browser)
    {
        _browser = browser;
    }

    /// <inheritdoc />
    public async Task<JraHorseProfileData?> ScrapeAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        await _browser.NavigateAsync(url, cancellationToken);
        return await ScrapeCurrentPageAsync(cancellationToken);
    }

    /// <summary>
    /// ブラウザが既に競走馬情報ページを表示している状態でページを解析する。
    /// クリックで遷移した直後に呼び出すことで URL ナビゲーションを省略できる。
    /// </summary>
    public async Task<JraHorseProfileData?> ScrapeCurrentPageAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: cancellationToken);
        var url = _browser.CurrentUrl ?? string.Empty;
        var kind = JraPageKindDetector.Detect(url, snapshot);

        if (kind != JraPageKind.HorseProfile)
        {
            return null;
        }

        var profile = JraProfileExtractor.ParseProfile(snapshot, url, kind);
        var raceHistory = ParseRaceHistoryTable(snapshot.Tables);

        return new JraHorseProfileData(profile, raceHistory);
    }

    // ------------------------------------------------------------------ //
    // 競走成績テーブルの解析
    // ------------------------------------------------------------------ //

    private static IReadOnlyList<JraHorseRaceHistoryEntryData> ParseRaceHistoryTable(
        IReadOnlyList<PageTableSnapshot> tables)
    {
        var table = tables.FirstOrDefault(IsHistoryTable);
        if (table is null)
        {
            return [];
        }

        var headers = table.Headers;
        var raceDateIndex = FindHeaderIndex(headers, "年月日", "日付");
        var racecourseIndex = FindHeaderIndex(headers, "競馬場", "開催");
        var raceNumberIndex = FindHeaderIndex(headers, "R");
        var raceNameIndex = FindHeaderIndex(headers, "レース名");
        var gateNumberIndex = FindHeaderIndex(headers, "枠番");
        var horseNumberIndex = FindHeaderIndex(headers, "馬番");
        var finishPositionIndex = FindHeaderIndex(headers, "着順", "着");
        var jockeyIndex = FindHeaderIndex(headers, "騎手名", "騎手");
        var weightIndex = FindHeaderIndex(headers, "斤量", "負担重量");
        var courseIndex = FindHeaderIndex(headers, "コース", "馬場");
        var distanceIndex = FindHeaderIndex(headers, "距離");
        var timeIndex = FindHeaderIndex(headers, "タイム");
        var marginIndex = FindHeaderIndex(headers, "着差");
        var last3FIndex = FindHeaderIndex(headers, "上り3F", "上がり3F", "上り", "後3F");
        var bodyWeightIndex = FindHeaderIndex(headers, "馬体重");
        var winnerIndex = FindHeaderIndex(headers, "勝ち馬", "2着馬");
        var prizeIndex = FindHeaderIndex(headers, "賞金");

        var entries = new List<JraHorseRaceHistoryEntryData>();
        foreach (var row in table.Rows)
        {
            if (row.Count == 0)
            {
                continue;
            }

            var raceName = NullIfEmpty(GetCell(row, raceNameIndex));
            if (raceName is null)
            {
                continue;
            }

            var distanceCell = GetCell(row, distanceIndex);
            var courseCell = GetCell(row, courseIndex);
            var surfaceCode = ExtractSurfaceCode(courseCell) ?? ExtractSurfaceCode(distanceCell);
            var distanceMeters = ExtractDistance(distanceCell) ?? ExtractDistance(courseCell);

            var (finishPosition, abnormalCode) = ParseFinishPosition(GetCell(row, finishPositionIndex));
            var (bodyWeight, bodyWeightDiff) = ParseBodyWeight(GetCell(row, bodyWeightIndex));

            entries.Add(new JraHorseRaceHistoryEntryData(
                RaceDate: ExtractRaceDate(GetCell(row, raceDateIndex)),
                Racecourse: ExtractRacecourse(GetCell(row, racecourseIndex)),
                RaceNumber: ParseInt(GetCell(row, raceNumberIndex)),
                RaceName: raceName,
                GateNumber: ParseInt(GetCell(row, gateNumberIndex)),
                HorseNumber: ParseInt(GetCell(row, horseNumberIndex)),
                FinishPosition: finishPosition,
                AbnormalResultCode: abnormalCode,
                JockeyName: NullIfEmpty(GetCell(row, jockeyIndex)),
                AssignedWeight: ParseDecimal(GetCell(row, weightIndex)),
                SurfaceCode: surfaceCode,
                DistanceMeters: distanceMeters,
                OfficialTime: NullIfEmpty(GetCell(row, timeIndex)),
                MarginText: NullIfEmpty(GetCell(row, marginIndex)),
                LastThreeFurlongTime: NullIfEmpty(GetCell(row, last3FIndex)),
                BodyWeight: bodyWeight,
                BodyWeightDiff: bodyWeightDiff,
                WinnerOrRunnerUpHorseName: NullIfEmpty(GetCell(row, winnerIndex)),
                PrizeMoney: ParseDecimal(GetCell(row, prizeIndex))));
        }

        return entries;
    }

    private static bool IsHistoryTable(PageTableSnapshot table) =>
        FindHeaderIndex(table.Headers, "レース名") >= 0;

    // ------------------------------------------------------------------ //
    // フィールド解析
    // ------------------------------------------------------------------ //

    private static DateOnly? ExtractRaceDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"(\d{4})[年/-](\d{1,2})[月/-](\d{1,2})日?");
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

    private static string? ExtractRacecourse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RacecourseNames.FirstOrDefault(rc => value.Contains(rc, StringComparison.Ordinal));
    }

    private static string? ExtractSurfaceCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Contains("ダート", StringComparison.Ordinal) || value.Contains("ダ", StringComparison.Ordinal))
        {
            return "ダート";
        }

        if (value.Contains("芝", StringComparison.Ordinal))
        {
            return "芝";
        }

        return null;
    }

    private static int? ExtractDistance(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"(?<distance>\d{3,4})");
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["distance"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dist)
            ? dist
            : null;
    }

    private static (int? finishPosition, string? abnormalCode) ParseFinishPosition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var trimmed = value.Trim();

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

    private static int FindHeaderIndex(IReadOnlyList<string> headers, params string[] candidates)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var normalizedHeader = NormalizeHeader(headers[i]);
            if (candidates.Any(c =>
            {
                var nc = NormalizeHeader(c);
                return normalizedHeader.Contains(nc, StringComparison.OrdinalIgnoreCase);
            }))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormalizeHeader(string? value)
        => new string((value ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c) && c != '　')
            .ToArray());

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
}
