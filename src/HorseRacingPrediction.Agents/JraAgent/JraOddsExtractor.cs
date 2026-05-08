using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// JRA オッズページから <see cref="JraOddsResult"/> を抽出する。
/// スナップショットのテーブルから 馬番・単勝オッズ・複勝オッズ を解析する。
/// </summary>
public sealed class JraOddsExtractor : IPageExtractor
{
    private static readonly Regex PlaceRangeRegex =
        new(@"(\d+(?:\.\d+)?)\s*[-〜～]\s*(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    public JraPageKind[] SupportedPageKinds => [JraPageKind.Odds];

    public async Task<object?> ExtractAsync(IWebBrowser browser, CancellationToken cancellationToken = default)
    {
        var snapshot = await browser.GetPageSnapshotAsync(cancellationToken: cancellationToken);
        var url = browser.CurrentUrl ?? string.Empty;
        return ParseOdds(snapshot, url);
    }

    private static JraOddsResult ParseOdds(PageSnapshot snapshot, string url)
    {
        var raceName = ExtractRaceName(snapshot);
        var winOdds = new List<JraWinOddsEntry>();
        var placeOdds = new List<JraPlaceOddsEntry>();

        foreach (var table in snapshot.Tables)
        {
            if (table.Headers.Count == 0) continue;

            var headers = table.Headers.Select(h => h.Trim()).ToList();

            var horseNoIdx  = FindHeaderIndex(headers, "馬番", "番号");
            var horseNmIdx  = FindHeaderIndex(headers, "馬名");
            var winIdx      = FindHeaderIndex(headers, "単勝");
            var popIdx      = FindHeaderIndex(headers, "人気");
            var placeIdx    = FindHeaderIndex(headers, "複勝");

            if (horseNoIdx < 0 || winIdx < 0) continue;

            foreach (var row in table.Rows)
            {
                var horseNoRaw = GetCell(row, horseNoIdx);
                if (!int.TryParse(horseNoRaw, out var horseNo)) continue;

                var horseName  = GetCell(row, horseNmIdx);
                var winRaw     = GetCell(row, winIdx);
                var popRaw     = GetCell(row, popIdx);
                var placeRaw   = GetCell(row, placeIdx);

                winOdds.Add(new JraWinOddsEntry(
                    horseNo,
                    NullIfEmpty(horseName),
                    ParseDecimal(winRaw),
                    ParseInt(popRaw)));

                if (placeIdx >= 0 && !string.IsNullOrWhiteSpace(placeRaw))
                {
                    var (min, max) = ParsePlaceRange(placeRaw);
                    placeOdds.Add(new JraPlaceOddsEntry(horseNo, NullIfEmpty(horseName), min, max));
                }
            }

            // テーブルが見つかったら終了（最初の有効テーブルを使う）
            if (winOdds.Count > 0) break;
        }

        return new JraOddsResult(
            RaceName: raceName,
            RaceDate: null,
            Racecourse: null,
            RaceNumber: null,
            WinOdds: winOdds,
            PlaceOdds: placeOdds,
            SourceUrl: url);
    }

    private static string? ExtractRaceName(PageSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Title)) return snapshot.Title.Trim();
        return snapshot.Headings.FirstOrDefault(h => h.Contains("R", StringComparison.Ordinal))?.Trim();
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, params string[] keywords)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (keywords.Any(k => headers[i].Contains(k, StringComparison.Ordinal)))
                return i;
        }
        return -1;
    }

    private static string GetCell(IReadOnlyList<string> row, int index)
    {
        if (index < 0 || index >= row.Count) return string.Empty;
        return row[index].Trim();
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "---" || raw == "-") return null;
        return decimal.TryParse(raw, out var v) ? v : null;
    }

    private static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw, out var v) ? v : null;
    }

    private static (decimal? Min, decimal? Max) ParsePlaceRange(string raw)
    {
        var m = PlaceRangeRegex.Match(raw);
        if (m.Success)
        {
            var min = decimal.TryParse(m.Groups[1].Value, out var lo) ? lo : (decimal?)null;
            var max = decimal.TryParse(m.Groups[2].Value, out var hi) ? hi : (decimal?)null;
            return (min, max);
        }
        var single = ParseDecimal(raw);
        return (single, single);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
