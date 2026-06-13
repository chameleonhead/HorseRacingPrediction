namespace HorseRacingPrediction.Scraping.Browser;

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
    /// 現在のページで指定ラベルに対応する選択項目を変更し、
    /// 更新後のページ本文テキストを返す。
    /// </summary>
    /// <param name="fieldText">選択対象フィールドのラベル</param>
    /// <param name="optionText">選択する表示値</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>選択後のページ本文テキスト</returns>
    Task<string> SelectOptionAsync(
        string fieldText,
        string optionText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定したセクション見出しの近傍にあるアクション要素をクリックし、
    /// 更新後のページ本文テキストを返す。
    /// </summary>
    /// <param name="sectionText">操作対象セクションを特定する見出しテキスト</param>
    /// <param name="actionText">クリックするアクションの表示テキスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>クリック後のページ本文テキスト</returns>
    Task<string> ClickActionInSectionAsync(
        string sectionText,
        string actionText,
        CancellationToken cancellationToken = default);

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
    Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(
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

    /// <summary>
    /// 現在ページに存在するフォーム構造を抽出する。
    /// </summary>
    Task<IReadOnlyList<PageFormSnapshot>> GetFormsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PageFormSnapshot>>([]);

    /// <summary>
    /// 指定したラベルまたは name に対応する入力要素へ値を設定する。
    /// </summary>
    Task<string> SetFieldValueAsync(
        string fieldLabelOrName,
        string value,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("SetFieldValueAsync is not implemented.");

    /// <summary>
    /// 指定したラベルまたは name に対応するチェックボックス状態を設定する。
    /// </summary>
    Task<string> SetCheckboxAsync(
        string fieldLabelOrName,
        bool isChecked,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("SetCheckboxAsync is not implemented.");

    /// <summary>
    /// 指定したフォーム（未指定時は先頭の可視フォーム）を送信する。
    /// </summary>
    Task<string> SubmitFormAsync(
        string? formLabel = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("SubmitFormAsync is not implemented.");

    private async Task<PageSnapshot> GetDefaultPageSnapshotAsync(
        int maxLinks,
        CancellationToken cancellationToken)
    {
        var url = CurrentUrl ?? string.Empty;
        var mainText = await GetPageContentAsync(cancellationToken);
        var links = await GetLinksAsync(maxLinks, cancellationToken);
        var rootSection = new PageSectionSnapshot(
            title: string.Empty,
            mainText: mainText,
            links: links.ToList(),
            actions: [],
            tables: [],
            forms: [],
            images: []);
        return new PageSnapshot(url, string.Empty, [rootSection]);
    }
}
