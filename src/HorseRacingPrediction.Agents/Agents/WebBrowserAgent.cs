using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// <see cref="PlaywrightTools"/> を使用して Web 上の情報を調査する汎用エージェント。
/// 他のエージェントから <see cref="InvokeAsync"/> を呼び出すことで使用できる。
/// また、<see cref="CreateAIFunction"/> で Microsoft Agent Framework の
/// <see cref="AIFunction"/> として取得し、他エージェントのツールとして登録することもできる。
/// </summary>
public sealed class WebBrowserAgent
{
    public const string AgentName = "WebBrowserAgent";

    public const string SystemPrompt = """
        あなたは Web 調査を行うブラウザエージェントです。
        ブラウザは常に開いた状態で、ページ間を移動しながら情報を収集します。
        収集した情報を日本語の Markdown で整理して返します。

        ## ツール一覧と使い方
        | ツール | 用途 |
        |--------|------|
        | BrowserSearch | 検索エンジンで検索し、結果リンクを取得する。検索後はそのページに留まる |
        | BrowserNavigate | サイトのトップページなど入口 URL を直接開く（検索結果の URL を開くのには使わない） |
        | BrowserClick | 現在のページのリンクやボタンをクリックして遷移する。検索結果のリンクもこれで開く |
        | BrowserGetLinks | 現在のページのリンク一覧を確認する |
        | BrowserGetPageContent | 現在のページのテキストを再取得する |
        | BrowserGoBack | 前のページに戻る |

        ## 行動手順
        1. BrowserSearch で検索し、関連リンクの一覧を得る（URLが指定されている場合はBrowserNavigateで直接開いてもよい）
        2. ページの内容を読み、さらに詳細が必要なら BrowserClick でリンクやボタンをたどる
        3. 行き止まりなら BrowserGoBack で戻って別のリンクを試す
        4. 目的の情報が得られるまでこれを繰り返す
        5. 目的の情報が得られたら、参照した URL を明記しつつ、Markdown で整理して返す

        ## 重要なルール
        - BrowserNavigate はサイトのトップページ（例: https://www.jra.go.jp/）を開くときだけ使う
        - 調査タスクの場合は、検索結果ページの参照で完了せず、必ずリンクをたどって実際のページを読んでから回答する
        - 検索結果やページ内のリンクは必ず BrowserClick で開く（直接 URL を指定しない）
        - URL を推測・生成しない。ツールが返した URL やリンクだけを使う
        - ページ本文を読んでから回答する。タイトルだけで判断しない
        - 参照した URL を回答に明記する
        """;

    private readonly ChatClientAgent _innerAgent;

    public WebBrowserAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// エージェントにメッセージを送り、応答を返す。
    /// </summary>
    /// <param name="userMessage">ユーザーからの依頼内容</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>エージェントの応答テキスト</returns>
    public async Task<string> InvokeAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(userMessage, cancellationToken: cancellationToken);
        return result.Text;
    }

    /// <summary>
    /// このエージェントを他のエージェントのツールとして使用するための
    /// <see cref="AIFunction"/> を生成する。
    /// 返された function を <c>tools</c> 一覧に追加することで、
    /// どのエージェントからも <c>WebBrowserAgent_BrowseWeb</c> として呼び出せる。
    /// </summary>
    public AIFunction CreateAIFunction() => _innerAgent.AsAIFunction();

    /// <summary>
    /// DI コンテナから <see cref="WebBrowserAgent"/> を構築するファクトリメソッド。
    /// <see cref="PlaywrightTools"/> をツールとして登録し、
    /// エージェントが Playwright ベースのブラウザ操作を自律的に使用できるようにする。
    /// </summary>
    public static WebBrowserAgent CreateFromServices(IServiceProvider services)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        var browser = services.GetRequiredService<IWebBrowser>();
        var options = services.GetRequiredService<IOptions<WebFetchOptions>>();
        var extractionAgent = services.GetService<PageDataExtractionAgent>();

        var playwrightTools = new PlaywrightTools(browser, options, extractionAgent);
        return new WebBrowserAgent(chatClient, playwrightTools.GetAITools());
    }
}
