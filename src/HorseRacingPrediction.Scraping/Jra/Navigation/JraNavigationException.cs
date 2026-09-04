namespace HorseRacingPrediction.Scraping.Jra.Navigation;

/// <summary>
/// <see cref="JraNavigationException"/> が発生した理由の分類。
/// 呼び出し元（Workflow/Collectorジョブ）が「リトライすべきか」「恒久的な失敗として
/// 記録すべきか」を判断できるようにするための情報であり、例外メッセージ自体の
/// 一致比較に依存させないために導入した。
/// </summary>
public enum JraNavigationFailureReason
{
    /// <summary>
    /// 上記以外、または未分類の失敗。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 対象日が今日より未来であり、開催選択ページに対象日の開催ボタンがまだ
    /// 掲載されていない（出馬表・レース結果がまだ公開されていない）ケース。
    /// これは業務的に正常な状態であり、時間を置いた再試行で解消し得る。
    /// </summary>
    NotYetPublished,

    /// <summary>
    /// 対象日が今日以前であり、かつ開催選択ページ（今週～直近数週間程度しか
    /// 掲載されない）の表示範囲外であるために対象日の開催ボタンが見つからない
    /// ケース。「過去レース結果検索」等の別導線へのフォールバックが必要。
    /// </summary>
    OutOfDisplayedRange,
}

/// <summary>
/// JRAナビゲーションが失敗したことを表す例外。
/// </summary>
public sealed class JraNavigationException
    : Exception
{
    public JraNavigationFailureReason Reason { get; }

    public JraNavigationException(
        string message)
        : this(message, JraNavigationFailureReason.Unknown)
    {
    }

    public JraNavigationException(
        string message,
        JraNavigationFailureReason reason)
        : base(message)
    {
        Reason = reason;
    }

    public JraNavigationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Reason = JraNavigationFailureReason.Unknown;
    }
}
