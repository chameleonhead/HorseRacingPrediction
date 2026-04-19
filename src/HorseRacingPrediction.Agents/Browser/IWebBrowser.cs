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

    /// <summary>
    /// ブラウザのアドレスバーから検索を行い、検索結果のリンク一覧を返す。
    /// 検索エンジンの URL を直接構築するのではなく、実際にブラウザの検索ボックスに
    /// クエリを入力して検索するため、ボット検知を回避しやすい。
    /// </summary>
    /// <param name="query">検索クエリ文字列（URL エンコード不要）</param>
    /// <param name="maxResults">抽出する最大リンク数</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検索結果のリンク一覧</returns>
    Task<IReadOnlyList<SearchResultLink>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default);
}
