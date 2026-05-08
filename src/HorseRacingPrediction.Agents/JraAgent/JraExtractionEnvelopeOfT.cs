namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// 型付けされた抽出結果。
/// データベース登録など後続処理で object キャストを不要にするための返却型。
/// </summary>
public sealed record JraExtractionEnvelope<T>(
    bool Success,
    JraPageKind PageKind,
    string SourceUrl,
    JraNavigationTrace Trace,
    T? Data,
    string? Error = null)
    where T : class
{
    /// <summary>失敗エンベロープを生成するファクトリ。</summary>
    public static JraExtractionEnvelope<T> Failure(
        JraPageKind kind,
        string url,
        JraNavigationTrace trace,
        string error)
        => new(false, kind, url, trace, null, error);
}