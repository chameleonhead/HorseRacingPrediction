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

    /// <summary>検索に使用するベース URL（Bing 等）</summary>
    public string SearchBaseUrl { get; set; } = "https://www.bing.com/search?q=";
}
