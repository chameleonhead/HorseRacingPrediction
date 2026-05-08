using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// JRA ページの URL とスナップショットから <see cref="JraPageKind"/> を判定する。
/// URL が利用可能な場合は URL を優先し、不明な場合はタイトル・本文で補完する。
/// </summary>
public static class JraPageKindDetector
{
    public static JraPageKind Detect(string? url, PageSnapshot? snapshot)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            var kind = DetectFromUrl(url);
            if (kind != JraPageKind.Unknown)
                return kind;
        }

        if (snapshot is not null)
            return DetectFromSnapshot(snapshot);

        return JraPageKind.Unknown;
    }

    private static JraPageKind DetectFromUrl(string url)
    {
        // JRADB accessX.html パターン
        if (url.Contains("accessD.html", StringComparison.OrdinalIgnoreCase))
            return JraPageKind.RaceCard;
        if (url.Contains("accessO.html", StringComparison.OrdinalIgnoreCase))
            return JraPageKind.Odds;
        if (url.Contains("accessP.html", StringComparison.OrdinalIgnoreCase))
            return JraPageKind.Result;
        if (url.Contains("accessU.html", StringComparison.OrdinalIgnoreCase))
            return JraPageKind.HorseProfile;
        if (url.Contains("accessJ.html", StringComparison.OrdinalIgnoreCase))
            return JraPageKind.JockeyProfile;
        if (url.Contains("accessT.html", StringComparison.OrdinalIgnoreCase))
            return JraPageKind.TrainerProfile;

        // 重賞・特別レース直リンク系（/syutsuba を含む URL）
        if (url.Contains("/syutsuba", StringComparison.OrdinalIgnoreCase))
            return JraPageKind.RaceCard;

        return JraPageKind.Unknown;
    }

    private static JraPageKind DetectFromSnapshot(PageSnapshot snapshot)
    {
        var title = snapshot.Title ?? string.Empty;
        var mainText = snapshot.MainText ?? string.Empty;

        if (title.Contains("出馬表", StringComparison.Ordinal)
            || snapshot.Headings.Any(h => h.Contains("出走馬", StringComparison.Ordinal)))
            return JraPageKind.RaceCard;

        if (title.Contains("オッズ", StringComparison.Ordinal)
            || mainText.Contains("単勝オッズ", StringComparison.Ordinal))
            return JraPageKind.Odds;

        if (title.Contains("払戻金", StringComparison.Ordinal)
            || title.Contains("レース結果", StringComparison.Ordinal)
            || mainText.Contains("着順", StringComparison.Ordinal) && mainText.Contains("払戻", StringComparison.Ordinal))
            return JraPageKind.Result;

        if (title.Contains("競走馬情報", StringComparison.Ordinal))
            return JraPageKind.HorseProfile;

        if (title.Contains("騎手情報", StringComparison.Ordinal))
            return JraPageKind.JockeyProfile;

        if (title.Contains("調教師情報", StringComparison.Ordinal))
            return JraPageKind.TrainerProfile;

        return JraPageKind.Unknown;
    }
}
