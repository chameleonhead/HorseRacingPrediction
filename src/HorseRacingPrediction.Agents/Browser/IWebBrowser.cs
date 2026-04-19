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
    /// <returns>ページの本文テキスト</returns>
    Task<string> FetchTextAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定した URL のページからリンク（&lt;a&gt; 要素の href）を抽出する。
    /// Bing・Google 等の検索結果ページに対応し、検索結果リンクを返す。
    /// </summary>
    /// <param name="url">リンクを抽出するページの URL</param>
    /// <param name="maxResults">抽出する最大リンク数</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>抽出されたリンク一覧</returns>
    Task<IReadOnlyList<SearchResultLink>> ExtractLinksAsync(
        string url,
        int maxResults = 10,
        CancellationToken cancellationToken = default);
}
