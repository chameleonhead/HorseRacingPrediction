using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// JRA 払戻金・レース結果ページから <see cref="JraRaceResultSummary"/> を抽出する。
/// 着順テーブルと払戻金テーブルをそれぞれ解析する。
/// </summary>
public sealed class JraRaceResultExtractor : IPageExtractor
{
    public JraPageKind[] SupportedPageKinds => [JraPageKind.Result];

    public async Task<object?> ExtractAsync(IWebBrowser browser, CancellationToken cancellationToken = default)
    {
        var snapshot = await browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: cancellationToken);
        var url = browser.CurrentUrl ?? string.Empty;
        return ParseResult(snapshot, url);
    }

    private static JraRaceResultSummary ParseResult(PageSnapshot snapshot, string url)
    {
        var raceName = snapshot.Title?.Trim();
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
            var jockeyIdx  = FindHeaderIndex(headers, "騎手");
            var timeIdx    = FindHeaderIndex(headers, "タイム", "走破時計");
            var weightIdx  = FindHeaderIndex(headers, "馬体重");

            if (horseNoIdx >= 0 && horseNmIdx >= 0 && entries.Count == 0)
            {
                foreach (var row in table.Rows)
                {
                    var horseNoRaw = GetCell(row, horseNoIdx);
                    if (!int.TryParse(horseNoRaw, out var horseNo)) continue;

                    entries.Add(new JraResultEntry(
                        FinishPosition: ParseInt(GetCell(row, posIdx)),
                        HorseNumber: horseNo,
                        GateNumber: ParseInt(GetCell(row, gateNoIdx)),
                        HorseName: NullIfEmpty(GetCell(row, horseNmIdx)),
                        JockeyName: NullIfEmpty(GetCell(row, jockeyIdx)),
                        FinishTime: NullIfEmpty(GetCell(row, timeIdx)),
                        Weight: ParseDecimal(GetCell(row, weightIdx))));
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
            RaceDate: null,
            Racecourse: null,
            RaceNumber: null,
            Entries: entries,
            Payouts: payouts,
            SourceUrl: url);
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

    private static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw, out var v) ? v : null;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "---") return null;
        // 馬体重: "480(+4)" のような形式への対応
        var idx = raw.IndexOf('(');
        if (idx >= 0) raw = raw[..idx];
        return decimal.TryParse(raw.Trim(), out var v) ? v : null;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
