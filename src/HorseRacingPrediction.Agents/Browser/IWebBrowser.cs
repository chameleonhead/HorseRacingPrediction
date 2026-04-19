namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// ブラウザを使ってウェブページのコンテンツを取得するインターフェース。
/// テスト時にモックへ差し替えられるよう DI で注入する。
/// </summary>
public interface IWebBrowser
{
    /// <summary>
    /// 指定した URL のページを JavaScript レンダリング後にテキスト本文として取得する。
    /// </summary>
    /// <param name="url">取得対象の URL</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ページの本文テキスト（最大 10,000 文字）</returns>
    Task<string> FetchTextAsync(string url, CancellationToken cancellationToken = default);
}
