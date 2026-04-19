namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// WebFetchTools が使用する設定オプション。
/// appsettings.json の "WebFetch" セクションにバインドする。
/// </summary>
public sealed class WebFetchOptions
{
    /// <summary>設定セクション名</summary>
    public const string SectionName = "WebFetch";

    /// <summary>
    /// アクセスを許可するホスト名（ドメイン）の一覧。
    /// 空の場合はすべてのドメインへのアクセスを拒否する。
    /// </summary>
    /// <example>["www.jra.go.jp", "db.netkeiba.com"]</example>
    public List<string> AllowedDomains { get; set; } = [];

    /// <summary>検索に使用するベース URL（Google 等）</summary>
    public string SearchBaseUrl { get; set; } = "https://www.google.co.jp/search?hl=ja&q=";

    /// <summary>検索結果からフェッチするページの最大件数</summary>
    public int SearchResultsToFetch { get; set; } = 3;
}
