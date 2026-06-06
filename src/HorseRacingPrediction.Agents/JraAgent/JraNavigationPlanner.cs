using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// ページ間遷移のヒント辞書と、スナップショットからクリック候補を選ぶロジックを提供する。
/// </summary>
public sealed class JraNavigationPlanner
{
    /// <summary>
    /// ページ種別間遷移に使うクリックテキスト候補（優先順）。
    /// </summary>
    private static readonly IReadOnlyDictionary<(JraPageKind From, JraPageKind To), string[]> PageTransitionHints
        = new Dictionary<(JraPageKind, JraPageKind), string[]>
        {
            [(JraPageKind.RaceCard, JraPageKind.Odds)]   = ["オッズ"],
            [(JraPageKind.RaceCard, JraPageKind.Result)]  = ["払戻金", "レース結果", "結果"],
            [(JraPageKind.Odds,     JraPageKind.RaceCard)] = ["出馬表"],
            [(JraPageKind.Odds,     JraPageKind.Result)]  = ["払戻金", "レース結果"],
            [(JraPageKind.Result,   JraPageKind.RaceCard)] = ["出馬表"],
            [(JraPageKind.Result,   JraPageKind.Odds)]    = ["オッズ"],
        };

    /// <summary>
    /// 現在ページから目的ページへの遷移ヒント文字列配列を返す。
    /// 直接遷移が定義されていない場合は null。
    /// </summary>
    public string[]? GetTransitionHints(JraPageKind from, JraPageKind to)
        => PageTransitionHints.TryGetValue((from, to), out var hints) ? hints : null;

    /// <summary>
    /// スナップショットの Actions → Links の順で、ヒント文字列のいずれかを含む最初の候補を返す。
    /// </summary>
    public string? FindBestClickTarget(PageSnapshot snapshot, string[] hints)
    {
        foreach (var hint in hints)
        {
            // ボタン・タブ系（Actions）を優先
            var action = snapshot.Actions
                .FirstOrDefault(a => a.Text?.Contains(hint, StringComparison.Ordinal) == true);
            if (action?.Text is { } aText)
                return aText.Trim();

            // リンク（Links）を次点
            var link = snapshot.Links
                .FirstOrDefault(l => l.Title?.Contains(hint, StringComparison.Ordinal) == true);
            if (link?.Title is { } lTitle)
                return lTitle.Trim();
        }
        return null;
    }

    /// <summary>
    /// スナップショットの中からエンティティ名（馬名・騎手名・調教師名）を含むリンクを探す。
    /// Actions → Links の順で検索する。
    /// </summary>
    public string? FindEntityLinkTarget(PageSnapshot snapshot, string entityName)
    {
        var action = snapshot.Actions
            .FirstOrDefault(a => a.Text?.Contains(entityName, StringComparison.Ordinal) == true);
        if (action?.Text is { } aText)
            return aText.Trim();

        var link = snapshot.Links
            .FirstOrDefault(l => l.Title?.Contains(entityName, StringComparison.Ordinal) == true);
        if (link?.Title is { } lTitle)
            return lTitle.Trim();

        return null;
    }
}
