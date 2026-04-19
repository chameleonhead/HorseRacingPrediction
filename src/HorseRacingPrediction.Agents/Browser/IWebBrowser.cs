namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// セッションベースのブラウザインターフェース。
/// ページはセッション中ずっと開いたままで、エージェントが
/// ナビゲーション・クリック・テキスト取得などの操作を逐次実行する。
/// テスト時にモックへ差し替えられるよう DI で注入する。
/// </summary>
public interface IWebBrowser : IAsyncDisposable
{
    /// <summary>
    /// 現在表示しているページの URL。初期状態では <c>null</c>。
    /// </summary>
    string? CurrentUrl { get; }

    /// <summary>
    /// 指定した URL に移動し、ページの本文テキストを返す。
    /// ページはセッション中再利用される。
    /// </summary>
    Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のページで指定テキストを持つ要素をクリックし、
    /// 遷移・更新後のページ本文テキストを返す。
    /// リンク・ボタン・タブなどインタラクティブ要素を操作できる。
    /// </summary>
    /// <param name="text">クリック対象の表示テキスト（部分一致）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>クリック後のページ本文テキスト</returns>
    Task<string> ClickAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のページの本文テキストを取得する。
    /// 動的コンテンツの再読み込みや、クリック後の確認に使用する。
    /// </summary>
    Task<string> GetPageContentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のページをモデル向けの構造化スナップショットとして取得する。
    /// 既定実装は本文とリンク一覧のみを利用する。
    /// </summary>
    Task<PageSnapshot> GetPageSnapshotAsync(
        int maxLinks = 0,
        CancellationToken cancellationToken = default)
        => GetDefaultPageSnapshotAsync(maxLinks, cancellationToken);

    /// <summary>
    /// 現在のページからリンク（&lt;a&gt; 要素の href）を抽出する。
    /// </summary>
    /// <param name="maxResults">抽出する最大リンク数</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>抽出されたリンク一覧</returns>
    Task<IReadOnlyList<SearchResultLink>> GetLinksAsync(
        int maxResults = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ブラウザの既定検索エンジンでクエリを実行し、検索結果ページのテキストを返す。
    /// 検索後、ブラウザは検索結果ページを表示した状態になるため、
    /// <see cref="ClickAsync"/> で検索結果のリンクをクリックできる。
    /// </summary>
    /// <param name="query">検索クエリ文字列</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検索結果ページの本文テキスト</returns>
    Task<string> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ブラウザの「戻る」を実行し、前のページの本文テキストを返す。
    /// </summary>
    Task<string> GoBackAsync(CancellationToken cancellationToken = default);

    private async Task<PageSnapshot> GetDefaultPageSnapshotAsync(
        int maxLinks,
        CancellationToken cancellationToken)
    {
        var url = CurrentUrl ?? string.Empty;
        var mainText = await GetPageContentAsync(cancellationToken);
        var links = await GetLinksAsync(maxLinks, cancellationToken);
        return new PageSnapshot(url, null, mainText, [], links, [], []);
    }
}
