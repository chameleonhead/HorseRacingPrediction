using System.ComponentModel;
using System.Text;
using HorseRacingPrediction.Agents.Agents;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// <see cref="WebBrowserAgent"/> に委譲して Web 検索・サイト探索・本文取得を行う汎用プラグイン。
/// 内部実装はエージェント＋<see cref="PlaywrightTools"/> で構成されており、
/// エージェントが自律的にページ移動・リンク探索・検索を行う。
/// <see cref="GetAITools"/> で <see cref="AITool"/> 一覧を取得し、
/// 外部エージェントのツールとして登録できる。
/// 競馬固有のツールは <see cref="HorseRacingTools"/> を参照。
/// </summary>
public sealed class WebFetchTools
{
    private readonly Func<string, CancellationToken, Task<string>> _invokeAgent;

    /// <summary>
    /// <see cref="WebBrowserAgent"/> を使用するプロダクション用コンストラクタ。
    /// </summary>
    public WebFetchTools(WebBrowserAgent agent)
    {
        _invokeAgent = agent.InvokeAsync;
    }

    /// <summary>
    /// テスト用コンストラクタ。エージェント呼び出しをデリゲートで差し替え可能にする。
    /// </summary>
    internal WebFetchTools(Func<string, CancellationToken, Task<string>> invokeAgent)
    {
        _invokeAgent = invokeAgent;
    }

    /// <summary>
    /// 指定した URL のページ本文を取得する。
    /// </summary>
    [Description("指定した URL のページ本文テキストを取得します。")]
    public async Task<string> FetchPageContent(
        [Description("取得対象のページ URL")] string url,
        CancellationToken cancellationToken = default)
    {
        return await _invokeAgent(
            $"次の URL のページ本文を取得してください。単一ページの取得で完結させ、ヘッダーやフッターなどの不要部分は除去しつつ本文はできるだけ元の表現を保ってください。\nURL: {url}",
            cancellationToken);
    }

    /// <summary>
    /// 自然言語クエリで検索を行い、上位サイトを調査して必要な情報を探す。
    /// </summary>
    [Description("自然言語で Web 検索を行い、上位サイトを順に調査しながら必要な情報が得られるまで探索します。")]
    public async Task<string> SearchWeb(
        [Description("検索クエリ文字列。自然言語で指定可能です")] string query,
        [Description("知りたい内容や調査目的。例: 料金体系、導入手順、出走馬一覧")] string? objective = null,
        [Description("検索対象を絞り込むサイト名やドメイン（省略可。例: docs.github.com）")] string? site = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Web 検索を行い、情報を収集して日本語の Markdown で返してください。");
        sb.AppendLine($"検索クエリ: {query}");
        if (!string.IsNullOrWhiteSpace(objective))
            sb.AppendLine($"調査目的: {objective}");
        if (!string.IsNullOrWhiteSpace(site))
            sb.AppendLine($"対象サイト: {site}");
        sb.AppendLine("URL が明示されていない場合は、対象サイト名やドメイン名が含まれていても、そのサイトをいきなり開かず必ず最初に検索結果一覧を取得してください。");
        sb.AppendLine("検索はブラウザのアドレスバー相当の検索を使ってください。");
        sb.AppendLine("検索結果のリンク一覧から関連度の高いページを選び、ページ取得は単一ページごとに完結させてください。");
        sb.AppendLine("詳細本文が同一ページ内の『詳細を表示』等のクリックでしか見えない場合だけ、その追加クリックを許可します。");
        sb.AppendLine("参照した URL を明記してください。URL は自分で推測せず、ツールが返した URL だけを使ってください。");
        return await _invokeAgent(sb.ToString(), cancellationToken);
    }

    /// <summary>
    /// 既知の URL を起点に、検索エンジンを介さずサイト内を探索する。
    /// </summary>
    [Description("既知の URL を入口として、そのサイト内を探索しながら目的に応じた情報を取得します。検索エンジンは使いません。")]
    public async Task<string> ExploreFromEntryPoint(
        [Description("探索の起点となる URL")] string entryUrl,
        [Description("知りたい内容や調査目的")] string objective,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("次の URL を起点にサイト内を探索し、情報を日本語の Markdown で返してください。");
        sb.AppendLine($"起点 URL: {entryUrl}");
        sb.AppendLine($"調査目的: {objective}");
        sb.AppendLine("検索エンジンは使わず、まず起点 URL の単一ページを取得してください。");
        sb.AppendLine("そのページだけで不足する場合に限り、関連リンク URL を使って別ページを 1 ページずつ読んでください。");
        sb.AppendLine("詳細本文が同一ページ内の『詳細を表示』等のクリックでしか見えない場合だけ、その追加クリックを許可します。");
        sb.AppendLine("参照した URL を明記してください。URL は自分で推測せず、ツールが返した URL だけを使ってください。");
        return await _invokeAgent(sb.ToString(), cancellationToken);
    }

    /// <summary>
    /// 検索結果の上位ページ本文をそのまま取得して返す簡易検索メソッド。
    /// </summary>
    [Description("検索クエリで検索エンジンを使い、上位の検索結果ページを実際に開いて本文テキストを取得します。")]
    public async Task<string> SearchAndFetch(
        [Description("検索クエリ文字列")] string query,
        [Description("検索対象を絞り込むサイト名（省略可。例: docs.github.com）")] string? site = null,
        CancellationToken cancellationToken = default)
    {
        return await SearchAndFetchContentAsync(query, site, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 検索エンジンで検索し、上位ページの本文を取得して返す。
    /// <see cref="SearchAndFetch"/> や <see cref="HorseRacingTools"/> から利用される。
    /// </summary>
    public async Task<string> SearchAndFetchContentAsync(
        string query,
        string? site = null,
        int? maxLinksToFetch = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("次の内容を検索し、見つけたページのテキスト本文をそのまま日本語で返してください。");
        sb.AppendLine($"検索クエリ: {query}");
        if (!string.IsNullOrWhiteSpace(site))
            sb.AppendLine($"対象サイト: {site}");
        if (maxLinksToFetch.HasValue)
            sb.AppendLine($"最大 {maxLinksToFetch} ページまで読んでください。");
        sb.AppendLine("URL が明示されていない場合は、対象サイト名やドメイン名が含まれていても、そのサイトをいきなり開かず必ず最初に検索結果一覧を取得してください。");
        sb.AppendLine("検索はブラウザのアドレスバー相当の検索を使い、検索結果リンクから対象 URL を選んでください。");
        sb.AppendLine("ページ取得は 1 ページずつ完結させ、不要部分だけ除去して本文はできるだけ元の表現を保ってください。");
        sb.AppendLine("URL は自分で推測せず、ツールが返した URL だけを使ってください。");
        return await _invokeAgent(sb.ToString(), cancellationToken);
    }

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(FetchPageContent),
        AIFunctionFactory.Create(SearchWeb),
        AIFunctionFactory.Create(ExploreFromEntryPoint),
        AIFunctionFactory.Create(SearchAndFetch)
    ];
}
